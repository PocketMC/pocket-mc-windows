using PocketMC.Application.Services.Instances;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Domain.Security;



using PocketMC.Infrastructure.Networking;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Infrastructure.Backups;

namespace PocketMC.Infrastructure.Backups;

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

    public async Task RunBackupAsync(InstanceMetadata metadata, string serverDir, bool isManualBackup = true, Action<string>? onProgress = null, IProgress<double>? progress = null)
    {
        var localResult = await CreateLocalBackupAsync(metadata, serverDir, onProgress, progress);

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
        var backupDir = GetBackupDirectory(serverDir, metadata);
        PruneOldBackups(backupDir, metadata.MaxBackupsToKeep);

        // Purge manifest entries whose files were pruned
        try
        {
            var manifest = BackupManifest.Load(serverDir);
            string defaultBackupDir = Path.Combine(serverDir, "backups");
            manifest.PurgeOrphanedEntries(defaultBackupDir, metadata.CustomBackupDirectory);
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

    private string? ComputeSha256(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not compute backup checksum for {FilePath}.", filePath);
            return null;
        }
    }

    /// <summary>
    /// Verify the integrity of a backup ZIP against its stored checksum.
    /// Returns null if no checksum is available, true if match, false if corrupt.
    /// </summary>
    public bool? VerifyBackupIntegrity(string serverDir, string fileName, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return false;
        }

        var manifest = BackupManifest.Load(serverDir);
        var entry = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (entry?.Sha256Checksum == null) return null;

        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return false;

        var currentHash = ComputeSha256(fullPath);
        return string.Equals(entry.Sha256Checksum, currentHash, StringComparison.OrdinalIgnoreCase);
    }

    private string GetBackupDirectory(string serverDir, InstanceMetadata? metadata)
    {
        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.CustomBackupDirectory))
        {
            return metadata.CustomBackupDirectory;
        }

        try
        {
            var metaFile = Path.Combine(serverDir, ".pocket-mc.json");
            if (File.Exists(metaFile))
            {
                var content = File.ReadAllText(metaFile);
                var meta = System.Text.Json.JsonSerializer.Deserialize<InstanceMetadata>(content);
                if (meta != null && !string.IsNullOrWhiteSpace(meta.CustomBackupDirectory))
                {
                    return meta.CustomBackupDirectory;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve custom backup directory for {ServerDir}; falling back to default backups directory.", serverDir);
        }
        return Path.Combine(serverDir, "backups");
    }

    private string? ResolveBackupFilePath(string serverDir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        string backupDir = GetBackupDirectory(serverDir, null);
        return PathSafety.ValidateContainedPath(backupDir, fileName);
    }

    private async Task<LocalBackupResult> CreateLocalBackupAsync(InstanceMetadata metadata, string serverDir, Action<string>? onProgress, IProgress<double>? progress)
    {
        string worldDir;
        string worldDisplayName;
        try
        {
            worldDir = ResolveWorldDirectory(metadata, serverDir);
            worldDisplayName = Path.GetRelativePath(serverDir, worldDir);
        }
        catch (Exception ex)
        {
            return new LocalBackupResult { Success = false, Error = ex };
        }

        if (!Directory.Exists(worldDir))
        {
            return new LocalBackupResult { Success = false, Error = new DirectoryNotFoundException($"World folder '{worldDisplayName}' not found in server directory.") };
        }

        var backupDir = GetBackupDirectory(serverDir, metadata);
        Directory.CreateDirectory(backupDir);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        string zipPath = Path.Combine(backupDir, $"world-{timestamp}.zip");

        // Safely delete pre-existing file if one exists at the same timestamp (or from a prior partial run)
        if (File.Exists(zipPath))
        {
            try
            {
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete pre-existing backup file at {ZipPath}", zipPath);
            }
        }

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
            await Task.Run(() => CreateZipWithLockedFileSkip(worldDir, zipPath, skippedFiles, progress));

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
                TryDeleteFile(zipPath, "partial backup ZIP");
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

    private string ResolveWorldDirectory(InstanceMetadata metadata, string serverDir)
    {
        if (metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Path.Combine(serverDir, "worlds");
        }

        if (metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true)
        {
            string levelName = "Bedrock level";
            if (_configService.TryGetProperty(serverDir, "level-name", out var configuredLevelName) &&
                !string.IsNullOrWhiteSpace(configuredLevelName))
            {
                levelName = configuredLevelName.Trim();
            }

            string safeLevelName = ValidateBedrockLevelName(levelName);
            string worldsRoot = Path.Combine(serverDir, "worlds");
            string? resolved = PathSafety.ValidateContainedPath(worldsRoot, safeLevelName);
            if (resolved == null)
            {
                throw new InvalidDataException("Bedrock level-name resolves outside the worlds directory. Refusing backup/restore for safety.");
            }

            return resolved;
        }

        return Path.Combine(serverDir, "world");
    }

    internal static string ValidateBedrockLevelName(string levelName)
    {
        string trimmed = levelName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidDataException("Bedrock level-name cannot be empty.");
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.IndexOfAny(new[] { '/', '\\', ':', '\0', '\r', '\n', '\t' }) >= 0 ||
            PathSafety.ContainsTraversal(trimmed) ||
            Path.IsPathRooted(trimmed))
        {
            throw new InvalidDataException($"Bedrock level-name '{trimmed}' is not safe to use as a world folder name.");
        }

        return trimmed;
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

    private void CreateZipWithLockedFileSkip(string sourceDir, string zipPath, List<string> skippedFiles, IProgress<double>? progress)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

        long totalBytes = 0;
        foreach (var filePath in allFiles)
        {
            if (!SkipFiles.Contains(Path.GetFileName(filePath)))
            {
                totalBytes += new FileInfo(filePath).Length;
            }
        }

        long copiedBytes = 0;
        byte[] buffer = new byte[81920]; // 80KB buffer

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
                
                int bytesRead;
                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    entryStream.Write(buffer, 0, bytesRead);
                    copiedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        progress?.Report((double)copiedBytes / totalBytes * 100);
                    }
                }
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
        string worldDir = ResolveWorldDirectory(metadata, serverDir);

        if (string.IsNullOrWhiteSpace(backupZipPath) || !File.Exists(backupZipPath))
        {
            throw new FileNotFoundException($"Backup ZIP file not found at '{backupZipPath}'");
        }

        onProgress?.Invoke("Verifying backup integrity...");
        try
        {
            using var archive = ZipFile.OpenRead(backupZipPath);
            _ = archive.Entries.Count; // Force reading ZIP contents to ensure it is not corrupt
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Backup ZIP file is corrupt or cannot be read.", ex);
        }

        var fileName = Path.GetFileName(backupZipPath);
        var integrity = VerifyBackupIntegrity(serverDir, fileName, backupZipPath);
        if (integrity == false)
        {
            throw new InvalidDataException("Backup ZIP file is corrupt (checksum mismatch).");
        }

        var stageDir = Path.Combine(serverDir, $".restore-stage-{Guid.NewGuid():N}");
        if (Directory.Exists(stageDir))
        {
            await FileUtils.CleanDirectoryAsync(stageDir);
        }

        onProgress?.Invoke("Extracting backup to staging area...");
        try
        {
            await SafeZipExtractor.ExtractAsync(backupZipPath, stageDir);
        }
        catch (Exception ex)
        {
            await TryCleanDirectoryAsync(stageDir, "restore staging directory");
            throw new InvalidDataException($"Failed to extract backup ZIP: {ex.Message}", ex);
        }

        onProgress?.Invoke("Validating world structure...");
        if (!HasLevelDat(stageDir))
        {
            await TryCleanDirectoryAsync(stageDir, "restore staging directory");
            throw new InvalidDataException("Could not find level.dat in the backup. This doesn't appear to be a valid Minecraft world backup.");
        }

        var backupWorldDir = Path.Combine(serverDir, $".restore-backup-{DateTime.Now:yyyyMMddHHmmss}");
        if (Directory.Exists(backupWorldDir))
        {
            await TryCleanDirectoryAsync(backupWorldDir, "pre-existing restore backup directory");
        }

        bool oldWorldExisted = Directory.Exists(worldDir);
        if (oldWorldExisted)
        {
            onProgress?.Invoke("Backing up current world...");
            try
            {
                Directory.Move(worldDir, backupWorldDir);
            }
            catch (Exception ex)
            {
                await TryCleanDirectoryAsync(stageDir, "restore staging directory");
                throw new IOException($"Failed to backup current world: {ex.Message}", ex);
            }
        }

        onProgress?.Invoke("Applying restored world...");
        try
        {
            Directory.Move(stageDir, worldDir);
        }
        catch (Exception ex)
        {
            onProgress?.Invoke("Restore failed, rolling back to original world...");
            if (oldWorldExisted && Directory.Exists(backupWorldDir))
            {
                try
                {
                    if (Directory.Exists(worldDir))
                    {
                        await TryCleanDirectoryAsync(worldDir, "failed restore world directory");
                    }
                    Directory.Move(backupWorldDir, worldDir);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx, "CRITICAL: Failed to roll back original world after restore failure.");
                }
            }
            await TryCleanDirectoryAsync(stageDir, "restore staging directory");
            throw new IOException($"Failed to move restored world into place: {ex.Message}", ex);
        }

        if (oldWorldExisted && Directory.Exists(backupWorldDir))
        {
            onProgress?.Invoke("Cleaning up backup...");
            try
            {
                await FileUtils.CleanDirectoryAsync(backupWorldDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up backup world directory at {BackupWorldDir}", backupWorldDir);
            }
        }

        onProgress?.Invoke("World restored successfully!");
    }

    private bool HasLevelDat(string dir)
    {
        if (File.Exists(Path.Combine(dir, "level.dat")))
            return true;

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            if (HasLevelDat(subDir))
                return true;
        }

        return false;
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

    private async Task TryCleanDirectoryAsync(string directory, string description)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                await FileUtils.CleanDirectoryAsync(directory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort cleanup could not clean {Description} at {Directory}.", description, directory);
        }
    }

    private void TryDeleteFile(string path, string description)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort cleanup could not delete {Description} at {Path}.", description, path);
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

