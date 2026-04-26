using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Instances.Backups;

/// <summary>
/// Handles world backup creation with safe save-lock handshake for live servers,
/// locked-file tolerance, and automatic retention pruning.
/// </summary>
public class BackupService
{
    private static readonly Regex SaveCompletedRegex = new(
        @"(saved the game|saved the world|saved chunks|saved all chunks|all dimensions are saved|world saved)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ServerProcessManager _serverProcessManager;
    private readonly ServerConfigurationService _configService;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        ServerProcessManager serverProcessManager, 
        ServerConfigurationService configService,
        SettingsManager settingsManager,
        ILogger<BackupService> logger)
    {
        _serverProcessManager = serverProcessManager;
        _configService = configService;
        _settingsManager = settingsManager;
        _logger = logger;
    }

    // Files the JVM holds exclusively — always skip these
    private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.lock"
    };

    /// <summary>
    /// Creates a timestamped world backup ZIP, using safe I/O synchronization
    /// if the server is currently running. Tolerates locked files gracefully.
    /// </summary>
    public async Task RunBackupAsync(InstanceMetadata metadata, string serverDir, Action<string>? onProgress = null)
    {
        string worldFolderName = "worlds";
        if (string.Equals(metadata.ServerType, "Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            if (_configService.TryGetProperty(serverDir, "level-name", out var levelName) && !string.IsNullOrWhiteSpace(levelName))
            {
                worldFolderName = levelName.Trim();
            }
            else
            {
                worldFolderName = "Bedrock level";
            }
        }

        var worldDir = Path.Combine(serverDir, worldFolderName);
        if (!Directory.Exists(worldDir))
        {
            throw new DirectoryNotFoundException($"World folder '{worldFolderName}' not found in server directory.");
        }

        var backupDir = Path.Combine(serverDir, "backups");
        Directory.CreateDirectory(backupDir);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        string zipPath = Path.Combine(backupDir, $"world-{timestamp}.zip");

        bool isRunning = _serverProcessManager.IsRunning(metadata.Id);
        var process = _serverProcessManager.GetProcess(metadata.Id);
        var skippedFiles = new List<string>();

        try
        {
            if (isRunning && process != null)
            {
                // Try RCON first as it's more reliable for state detection
                bool syncSuccess = await TrySyncSaveViaRconAsync(serverDir, onProgress);

                if (!syncSuccess)
                {
                    _logger.LogInformation("RCON sync not available or failed; falling back to console ingestion for server {ServerName}.", metadata.Name);
                    
                    // Fallback to console handshake
                    onProgress?.Invoke("Disabling auto-save (Console)...");
                    await process.WriteInputAsync("save-off");
                    await Task.Delay(500);

                    onProgress?.Invoke("Flushing world to disk (Console)...");
                    await process.WriteInputAsync("save-all");

                    onProgress?.Invoke("Waiting for save to complete...");
                    bool saved = await process.WaitForConsoleOutputAsync(SaveCompletedRegex, TimeSpan.FromSeconds(15));

                    if (!saved)
                    {
                        _logger.LogWarning(
                            "Server {ServerName} did not emit a recognized save confirmation. Proceeding after a short settle delay.",
                            metadata.Name);
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                }
            }

            // Compress world folder with locked-file tolerance
            onProgress?.Invoke("Compressing world...");
            await Task.Run(() => CreateZipWithLockedFileSkip(worldDir, zipPath, skippedFiles));

            // Verify we actually wrote something meaningful
            var zipInfo = new FileInfo(zipPath);
            if (!zipInfo.Exists || zipInfo.Length == 0)
            {
                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete empty backup archive {ZipPath}.", zipPath);
                }

                throw new IOException("Backup produced an empty ZIP file.");
            }

            if (skippedFiles.Count > 0)
                onProgress?.Invoke($"Backup complete! ({skippedFiles.Count} locked file(s) skipped)");
            else
                onProgress?.Invoke("Backup complete!");

            // External Disaster Recovery: replicate payload to configured off-site path
            var appSettings = _settingsManager.Load();
            if (!string.IsNullOrWhiteSpace(appSettings.ExternalBackupDirectory) && Directory.Exists(appSettings.ExternalBackupDirectory))
            {
                onProgress?.Invoke("Replicating to external storage...");
                try
                {
                    string externalTarget = Path.Combine(appSettings.ExternalBackupDirectory, metadata.Name, "backups");
                    Directory.CreateDirectory(externalTarget);
                    
                    string destinationPath = Path.Combine(externalTarget, $"world-{timestamp}.zip");
                    await Task.Run(() => File.Copy(zipPath, destinationPath, true));
                    _logger.LogInformation("Successfully replicated backup to external location: {Destination}", destinationPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to replicate backup to external directory {Dir}", appSettings.ExternalBackupDirectory);
                    onProgress?.Invoke("Warning: External replication failed (Check logs)");
                }
            }
        }
        catch (Exception ex)
        {
            // Clean up partial/failed ZIP so 0-byte ghosts don't appear in the list
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up partial backup archive {ZipPath}.", zipPath);
            }

            _logger.LogError(ex, "Backup failed for server {ServerName}.", metadata.Name);
            throw;
        }
        finally
        {
            // Always re-enable auto-save if server was running
            if (isRunning && process != null)
            {
                try
                {
                    // Try RCON first
                    if (!_configService.TryGetProperty(serverDir, "enable-rcon", out var rconEnabled) || rconEnabled != "true")
                    {
                        await process.WriteInputAsync("save-on");
                    }
                    else
                    {
                        // We could use RCON here too, but save-on is safe via console
                        await process.WriteInputAsync("save-on");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to re-enable auto-save after backing up server {ServerName}.", metadata.Name);
                }
            }
        }

        // Update metadata
        metadata.LastBackupTime = DateTime.UtcNow;
        SaveMetadata(metadata, serverDir);

        // Prune old backups
        PruneOldBackups(backupDir, metadata.MaxBackupsToKeep);
    }

    private async Task<bool> TrySyncSaveViaRconAsync(string serverDir, Action<string>? onProgress)
    {
        try
        {
            if (!_configService.TryGetProperty(serverDir, "enable-rcon", out var rconEnabled) || rconEnabled != "true")
            {
                return false;
            }

            _configService.TryGetProperty(serverDir, "rcon.port", out var portStr);
            _configService.TryGetProperty(serverDir, "rcon.password", out var password);

            if (string.IsNullOrEmpty(password) || !int.TryParse(portStr ?? "25575", out int port))
            {
                return false;
            }

            onProgress?.Invoke("Connecting to RCON...");
            using var rcon = new RconClient("127.0.0.1", port, password);
            await rcon.ConnectAsync();

            onProgress?.Invoke("Syncing via RCON: save-off");
            await rcon.ExecuteCommandAsync("save-off");
            
            onProgress?.Invoke("Syncing via RCON: save-all");
            var response = await rcon.ExecuteCommandAsync("save-all");
            
            _logger.LogInformation("RCON save-all response: {Response}", response);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RCON sync failed.");
            return false;
        }
    }

    /// <summary>
    /// Builds a ZIP manually, opening each file with FileShare.ReadWrite
    /// so JVM-locked region files can still be read, and skipping any
    /// file that is exclusively locked (like session.lock).
    /// </summary>
    private void CreateZipWithLockedFileSkip(string sourceDir, string zipPath, List<string> skippedFiles)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

        foreach (var filePath in allFiles)
        {
            string relativePath = Path.GetRelativePath(sourceDir, filePath);
            string fileName = Path.GetFileName(filePath);

            // Always skip known locked files
            if (SkipFiles.Contains(fileName))
            {
                skippedFiles.Add(relativePath);
                continue;
            }

            try
            {
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                // FileShare.ReadWrite allows reading files the JVM has open
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fileStream.CopyTo(entryStream);
            }
            catch (IOException)
            {
                // File is exclusively locked — skip it rather than fail the whole backup
                skippedFiles.Add(relativePath);
            }
            catch (UnauthorizedAccessException)
            {
                skippedFiles.Add(relativePath);
            }
        }
    }

    /// <summary>
    /// Restores a backup by replacing the world folder with the backup contents.
    /// Server MUST be stopped before calling this.
    /// </summary>
    public async Task RestoreBackupAsync(InstanceMetadata metadata, string backupZipPath, string serverDir, Action<string>? onProgress = null)
    {
        string worldFolderName = "world";
        if (string.Equals(metadata.ServerType, "Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            if (_configService.TryGetProperty(serverDir, "level-name", out var levelName) && !string.IsNullOrWhiteSpace(levelName))
            {
                worldFolderName = levelName.Trim();
            }
            else
            {
                worldFolderName = "Bedrock level";
            }
        }

        var worldDir = Path.Combine(serverDir, worldFolderName);

        onProgress?.Invoke("Removing current world...");
        if (Directory.Exists(worldDir))
        {
            await FileUtils.CleanDirectoryAsync(worldDir);
        }

        onProgress?.Invoke("Extracting backup...");
        // SEC-02: Use SafeZipExtractor to prevent zip-slip path traversal
        await SafeZipExtractor.ExtractAsync(backupZipPath, worldDir);

        onProgress?.Invoke("World restored successfully!");
    }

    /// <summary>
    /// Deletes oldest backups that exceed the retention limit.
    /// </summary>
    private void PruneOldBackups(string backupDirectory, int maxToKeep)
    {
        var files = new DirectoryInfo(backupDirectory)
            .GetFiles("world-*.zip")
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        for (int i = maxToKeep; i < files.Count; i++)
        {
            try
            {
                files[i].Delete();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune old backup {BackupFile}.", files[i].FullName);
            }
        }
    }

    private void SaveMetadata(InstanceMetadata metadata, string serverDir)
    {
        var metaFile = Path.Combine(serverDir, ".pocket-mc.json");
        var json = System.Text.Json.JsonSerializer.Serialize(metadata,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        FileUtils.AtomicWriteAllText(metaFile, json);
    }
}
