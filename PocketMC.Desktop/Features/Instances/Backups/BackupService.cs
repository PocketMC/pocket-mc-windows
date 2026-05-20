using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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
using PocketMC.Desktop.Features.CloudBackups;

namespace PocketMC.Desktop.Features.Instances.Backups;

public class LocalBackupResult
{
    public bool Success { get; set; }
    public string ZipPath { get; set; } = string.Empty;
    public Exception? Error { get; set; }
}

public class BackupService
{
    private static readonly Regex SaveCompletedRegex = new(
        @"(saved the game|saved the world|saved chunks|saved all chunks|all dimensions are saved|world saved)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly ServerProcessManager _serverProcessManager;
    private readonly ServerConfigurationService _configService;
    private readonly SettingsManager _settingsManager;
    private readonly CloudBackupService _cloudBackupService;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        ServerProcessManager serverProcessManager, 
        ServerConfigurationService configService,
        SettingsManager settingsManager,
        CloudBackupService cloudBackupService,
        ILogger<BackupService> logger)
    {
        _serverProcessManager = serverProcessManager;
        _configService = configService;
        _settingsManager = settingsManager;
        _cloudBackupService = cloudBackupService;
        _logger = logger;
    }

    private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.lock"
    };

    public async Task RunBackupAsync(InstanceMetadata metadata, string serverDir, bool isManualBackup = true, Action<string>? onProgress = null)
    {
        var localResult = await CreateLocalBackupAsync(metadata, serverDir, onProgress);
        
        if (!localResult.Success || string.IsNullOrEmpty(localResult.ZipPath))
        {
            // Record failure in manifest for health warnings
            RecordBackupFailure(serverDir, localResult.Error?.Message ?? "Unknown error");
            if (localResult.Error != null) throw localResult.Error;
            return;
        }

        // Record metadata entry for this backup
        onProgress?.Invoke("Recording backup metadata...");
        RecordBackupMetadata(metadata, serverDir, localResult.ZipPath, isManualBackup);

        // External replication
        await ReplicateToExternalDirectoryAsync(metadata, localResult.ZipPath, onProgress);

        // Cloud replication
        await _cloudBackupService.UploadBackupToEnabledProvidersAsync(
            metadata.Id, 
            metadata.Name, 
            localResult.ZipPath, 
            isManualBackup, 
            onProgress);

        // Update metadata
        metadata.LastBackupTime = DateTime.UtcNow;
        SaveMetadata(metadata, serverDir);

        // Prune old backups
        var backupDir = Path.Combine(serverDir, "backups");
        PruneOldBackups(backupDir, metadata.MaxBackupsToKeep);

        // Purge manifest entries whose files were pruned
        try
        {
            var manifest = BackupManifest.Load(serverDir);
            manifest.PurgeOrphanedEntries(serverDir);
            manifest.Save(serverDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to purge orphaned manifest entries.");
        }
    }

    private void RecordBackupMetadata(InstanceMetadata metadata, string serverDir, string zipPath, bool isManual)
    {
        try
        {
            var manifest = BackupManifest.Load(serverDir);
            var fi = new FileInfo(zipPath);
            var previousEntry = manifest.Entries
                .OrderByDescending(e => e.Version)
                .FirstOrDefault();

            var entry = new BackupMetadataEntry
            {
                FileName = fi.Name,
                CreatedAtUtc = DateTime.UtcNow,
                Trigger = isManual ? BackupTrigger.Manual : BackupTrigger.Scheduled,
                SizeBytes = fi.Length,
                ServerType = metadata.ServerType ?? "Unknown",
                MinecraftVersion = metadata.MinecraftVersion ?? "Unknown",
                Version = manifest.GetNextVersion(),
                SizeDeltaBytes = previousEntry != null ? fi.Length - previousEntry.SizeBytes : null,
                Sha256Checksum = ComputeSha256(zipPath),
                IntegrityVerified = true
            };

            manifest.Entries.Add(entry);

            // Clear failure state on success
            manifest.LastFailedBackupUtc = null;
            manifest.LastFailureReason = null;

            manifest.Save(serverDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record backup metadata for {ZipPath}.", zipPath);
        }
    }

    private void RecordBackupFailure(string serverDir, string reason)
    {
        try
        {
            var manifest = BackupManifest.Load(serverDir);
            manifest.LastFailedBackupUtc = DateTime.UtcNow;
            manifest.LastFailureReason = reason;
            manifest.Save(serverDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record backup failure.");
        }
    }

    private static string? ComputeSha256(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verify the integrity of a backup ZIP against its stored checksum.
    /// Returns null if no checksum is available, true if match, false if corrupt.
    /// </summary>
    public bool? VerifyBackupIntegrity(string serverDir, string fileName)
    {
        var manifest = BackupManifest.Load(serverDir);
        var entry = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (entry?.Sha256Checksum == null) return null;

        string? zipPath = ResolveBackupFilePath(serverDir, fileName);
        if (zipPath == null || !File.Exists(zipPath)) return false;

        var currentHash = ComputeSha256(zipPath);
        return string.Equals(entry.Sha256Checksum, currentHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveBackupFilePath(string serverDir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        string backupDir = Path.Combine(serverDir, "backups");
        return PathSafety.ValidateContainedPath(backupDir, fileName);
    }

    private async Task<LocalBackupResult> CreateLocalBackupAsync(InstanceMetadata metadata, string serverDir, Action<string>? onProgress)
    {
        string worldFolderName = "world";
        if (metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true)
        {
            worldFolderName = "worlds";
        }
        else if (metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (_configService.TryGetProperty(serverDir, "level-name", out var levelName) && !string.IsNullOrWhiteSpace(levelName))
            {
                worldFolderName = Path.Combine("worlds", levelName.Trim());
            }
            else
            {
                worldFolderName = Path.Combine("worlds", "Bedrock level");
            }
        }

        var worldDir = Path.Combine(serverDir, worldFolderName);
        if (!Directory.Exists(worldDir))
        {
            return new LocalBackupResult { Success = false, Error = new DirectoryNotFoundException($"World folder '{worldFolderName}' not found in server directory.") };
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
                bool syncSuccess = await TrySyncSaveViaRconAsync(serverDir, onProgress);

                if (!syncSuccess)
                {
                    _logger.LogInformation("RCON sync not available or failed; falling back to console ingestion for server {ServerName}.", metadata.Name);
                    
                    onProgress?.Invoke("Disabling auto-save (Console)...");
                    await process.WriteInputAsync("save-off");
                    await Task.Delay(500);

                    onProgress?.Invoke("Flushing world to disk (Console)...");
                    await process.WriteInputAsync("save-all");

                    onProgress?.Invoke("Waiting for save to complete...");
                    bool saved = await process.WaitForConsoleOutputAsync(SaveCompletedRegex, TimeSpan.FromSeconds(15));

                    if (!saved)
                    {
                        _logger.LogWarning("Server {ServerName} did not emit a recognized save confirmation. Proceeding after a short settle delay.", metadata.Name);
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                }
            }

            onProgress?.Invoke("Compressing world...");
            await Task.Run(() => CreateZipWithLockedFileSkip(worldDir, zipPath, skippedFiles));

            var zipInfo = new FileInfo(zipPath);
            if (!zipInfo.Exists || zipInfo.Length == 0)
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                return new LocalBackupResult { Success = false, Error = new IOException("Backup produced an empty ZIP file.") };
            }

            if (skippedFiles.Count > 0)
                onProgress?.Invoke($"Backup complete! ({skippedFiles.Count} locked file(s) skipped)");
            else
                onProgress?.Invoke("Backup complete!");

            return new LocalBackupResult { Success = true, ZipPath = zipPath };
        }
        catch (Exception ex)
        {
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }
            _logger.LogError(ex, "Backup failed for server {ServerName}.", metadata.Name);
            return new LocalBackupResult { Success = false, Error = ex };
        }
        finally
        {
            if (isRunning && process != null)
            {
                try
                {
                    await process.WriteInputAsync("save-on");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to re-enable auto-save.");
                }
            }
        }
    }

    private async Task ReplicateToExternalDirectoryAsync(InstanceMetadata metadata, string zipPath, Action<string>? onProgress)
    {
        var appSettings = _settingsManager.Load();
        if (!string.IsNullOrWhiteSpace(appSettings.ExternalBackupDirectory) && Directory.Exists(appSettings.ExternalBackupDirectory))
        {
            onProgress?.Invoke("Replicating to external storage...");
            try
            {
                string timestamp = Path.GetFileNameWithoutExtension(zipPath).Replace("world-", "");
                string externalTarget = Path.Combine(appSettings.ExternalBackupDirectory, metadata.Name, "backups");
                Directory.CreateDirectory(externalTarget);
                
                string destinationPath = Path.Combine(externalTarget, $"world-{timestamp}.zip");
                await Task.Run(() => File.Copy(zipPath, destinationPath, true));
                _logger.LogInformation("Successfully replicated backup to external location: {Destination}", destinationPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replicate backup to external directory");
                onProgress?.Invoke("Warning: External replication failed (Check logs)");
            }
        }
    }

    private async Task<bool> TrySyncSaveViaRconAsync(string serverDir, Action<string>? onProgress)
    {
        try
        {
            if (!_configService.TryGetProperty(serverDir, "enable-rcon", out var rconEnabled) || rconEnabled != "true") return false;

            _configService.TryGetProperty(serverDir, "rcon.port", out var portStr);
            _configService.TryGetProperty(serverDir, "rcon.password", out var password);

            if (string.IsNullOrEmpty(password) || !int.TryParse(portStr ?? "25575", out int port)) return false;

            onProgress?.Invoke("Connecting to RCON...");
            using var rcon = new RconClient("127.0.0.1", port, password);
            await rcon.ConnectAsync();

            onProgress?.Invoke("Syncing via RCON: save-off");
            await rcon.ExecuteCommandAsync("save-off");
            
            onProgress?.Invoke("Syncing via RCON: save-all");
            var response = await rcon.ExecuteCommandAsync("save-all");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RCON sync failed.");
            return false;
        }
    }

    private void CreateZipWithLockedFileSkip(string sourceDir, string zipPath, List<string> skippedFiles)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

        foreach (var filePath in allFiles)
        {
            string relativePath = Path.GetRelativePath(sourceDir, filePath);
            string fileName = Path.GetFileName(filePath);

            if (SkipFiles.Contains(fileName))
            {
                skippedFiles.Add(relativePath);
                continue;
            }

            try
            {
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fileStream.CopyTo(entryStream);
            }
            catch (IOException)
            {
                skippedFiles.Add(relativePath);
            }
            catch (UnauthorizedAccessException)
            {
                skippedFiles.Add(relativePath);
            }
        }
    }

    public async Task RestoreBackupAsync(InstanceMetadata metadata, string backupZipPath, string serverDir, Action<string>? onProgress = null)
    {
        string worldFolderName = "world";
        if (metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true)
        {
            worldFolderName = "worlds";
        }
        else if (metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (_configService.TryGetProperty(serverDir, "level-name", out var levelName) && !string.IsNullOrWhiteSpace(levelName))
            {
                worldFolderName = Path.Combine("worlds", levelName.Trim());
            }
            else
            {
                worldFolderName = Path.Combine("worlds", "Bedrock level");
            }
        }

        var worldDir = Path.Combine(serverDir, worldFolderName);

        onProgress?.Invoke("Removing current world...");
        if (Directory.Exists(worldDir))
        {
            await FileUtils.CleanDirectoryAsync(worldDir);
        }

        onProgress?.Invoke("Extracting backup...");
        await SafeZipExtractor.ExtractAsync(backupZipPath, worldDir);

        onProgress?.Invoke("World restored successfully!");
    }

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
