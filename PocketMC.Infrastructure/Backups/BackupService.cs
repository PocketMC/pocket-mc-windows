using PocketMC.Infrastructure.Configuration;
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
    private readonly InstanceOperationCoordinator _operations;

    public BackupService(ServerProcessManager processManager, ServerConfigurationService configurationService,
        SettingsManager settingsManager, CloudBackupService cloudService, ILogger<BackupService> logger)
        : this(processManager, configurationService, settingsManager, cloudService, logger, null) { }

    public BackupService(
        ServerProcessManager serverProcessManager,
        ServerConfigurationService configService,
        SettingsManager settingsManager,
        CloudBackupService cloudBackupService,
        ILogger<BackupService> logger, InstanceOperationCoordinator? operations)
    {
        _serverProcessManager = serverProcessManager;
        _configService = configService;
        _settingsManager = settingsManager;
        _cloudBackupService = cloudBackupService;
        _logger = logger;
        _operations = operations ?? new();
    }

    private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.lock"
    };

    public virtual async Task RunBackupAsync(InstanceMetadata metadata, string serverDir, bool isManualBackup = true, Action<string>? onProgress = null, IProgress<double>? progress = null)
    {
        using var operation = await _operations.AcquireAsync(serverDir);
        InstanceFileTransaction.Recover(serverDir);
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
            onProgress,
            progress);

        // Update metadata
        metadata.LastBackupTime = DateTime.UtcNow;
        SaveMetadata(metadata, serverDir);

        // Prune old backups
        var backupDir = GetBackupDirectory(serverDir, metadata);
        PruneOldBackups(backupDir, Math.Max(1, metadata.MaxBackupsToKeep), metadata.Id);

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
                IntegrityVerified = false // A checksum is not a restore drill.
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

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss-fffffff");
        string completedPath = Path.Combine(backupDir, $"world-{metadata.Id:N}-{timestamp}-{Guid.NewGuid():N}.zip");
        string zipPath = completedPath + ".partial";
        var roots = WorldInventory.Resolve(serverDir, metadata);
        foreach (var root in roots)
        {
            string rel = Path.GetRelativePath(root.DirectoryPath, Path.GetFullPath(backupDir));
            if (!rel.StartsWith(".." + Path.DirectorySeparatorChar) && rel != ".." && !Path.IsPathRooted(rel))
                throw new InvalidOperationException("The backup destination cannot be inside a world directory.");
        }

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
                if (!metadata.Compatibility.IsJavaEngine)
                    throw new InvalidOperationException("Stop Bedrock or PocketMine before backing up. Live snapshots require an engine-specific save protocol.");
                if (!await TrySyncSaveViaRconAsync(serverDir, onProgress))
                    throw new InvalidOperationException("Live backups require RCON and a confirmed flushed save. Stop the server or configure RCON.");
            }

            onProgress?.Invoke("Compressing world...");
            await Task.Run(() => CreateWorldArchive(roots, zipPath, progress));

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

            FileUtils.PromoteFile(zipPath, completedPath);
            return new LocalBackupResult { Success = true, ZipPath = completedPath };
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
            if (isRunning && process != null && metadata.Compatibility.IsJavaEngine && _serverProcessManager.IsRunning(metadata.Id))
            {
                try
                {
                    if (!_configService.TryGetProperty(serverDir, "rcon.password", out string? password) || string.IsNullOrEmpty(password))
                        throw new InvalidOperationException("RCON credentials are missing.");
                    _configService.TryGetProperty(serverDir, "rcon.port", out string? rawPort);
                    using var rcon = new RconClient("127.0.0.1", int.Parse(rawPort ?? "25575"), password);
                    await rcon.ConnectAsync();
                    string response = await rcon.ExecuteCommandAsync("save-on");
                    if (!response.Contains("enabled", StringComparison.OrdinalIgnoreCase) && !response.Contains("already", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The server did not confirm save-on.");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "World saving could not be resumed. Run save-on on the server immediately.");
                    throw new InvalidOperationException("World saving could not be resumed. Run save-on on the server immediately.", ex);
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

        return WorldInventory.Resolve(serverDir, metadata)[0].DirectoryPath;
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
            string disabled = await rcon.ExecuteCommandAsync("save-off");
            if (!disabled.Contains("disabled", StringComparison.OrdinalIgnoreCase) && !disabled.Contains("already", StringComparison.OrdinalIgnoreCase)) return false;

            onProgress?.Invoke("Syncing via RCON: save-all");
            var response = await rcon.ExecuteCommandAsync("save-all flush");

            return response.Contains("saved", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RCON sync failed.");
            return false;
        }
    }

    private static void CreateWorldArchive(IReadOnlyList<WorldRoot> roots, string zipPath, IProgress<double>? progress)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using (var manifest = archive.CreateEntry(".pocketmc-worlds.json").Open())
            System.Text.Json.JsonSerializer.Serialize(manifest, roots.Select(r => r.RelativePath).ToArray());
        foreach (var root in roots)
        {
            PathSafety.RequireNoLinks(root.DirectoryPath);
            foreach (var file in FileUtils.EnumerateFilesNoLinks(root.DirectoryPath))
            {
                if (SkipFiles.Contains(Path.GetFileName(file))) continue;
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var entry = archive.CreateEntry(root.RelativePath + "/" + Path.GetRelativePath(root.DirectoryPath, file).Replace('\\', '/'), CompressionLevel.Fastest);
                using var output = entry.Open();
                input.CopyTo(output); // Any required-file failure aborts the entire candidate.
            }
        }
        progress?.Report(100);
    }

    public async Task RestoreBackupAsync(InstanceMetadata metadata, string backupZipPath, string serverDir, Action<string>? onProgress = null, IProgress<double>? progress = null)
    {
        using var operation = await _operations.AcquireAsync(serverDir, requiresStopped: true);
        if (_serverProcessManager?.IsRunning(metadata.Id) == true) throw new InvalidOperationException("Stop the server before restoring a backup.");
        InstanceFileTransaction.Recover(serverDir);
        string worldDir = ResolveWorldDirectory(metadata, serverDir);

        if (string.IsNullOrWhiteSpace(backupZipPath) || !File.Exists(backupZipPath))
        {
            throw new FileNotFoundException("Backup file not found.", backupZipPath);
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
            await SafeZipExtractor.ExtractAsync(backupZipPath, stageDir, (current, total) =>
            {
                if (total > 0)
                {
                    double percent = (double)current / total * 100.0;
                    progress?.Report(percent);
                }
            });
        }
        catch (Exception ex)
        {
            await TryCleanDirectoryAsync(stageDir, "restore staging directory");
            throw new InvalidDataException($"Failed to extract backup ZIP: {ex.Message}", ex);
        }

        try
        {
            var replacements = new List<InstanceFileTransaction.Replacement>();
            string layout = Path.Combine(stageDir, ".pocketmc-worlds.json");
            if (File.Exists(layout))
            {
                var roots = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(layout)) ?? throw new InvalidDataException("Missing world layout.");
                if (roots.Length == 0 || roots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != roots.Length) throw new InvalidDataException("Invalid world layout.");
                foreach (string relative in roots)
                {
                    string source = PathSafety.ValidateContainedPath(stageDir, relative) ?? throw new InvalidDataException("Unsafe world root.");
                    if (!HasLevelDat(source)) throw new InvalidDataException("Backup world is missing level.dat.");
                    // Never permit an archive layout to replace the instance/config/runtime roots.
                    string first = relative.Split('/', '\\')[0];
                    if (first.StartsWith('.') || new[] { "plugins", "mods", "libraries", "config", "backups", "runtime" }.Contains(first, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("Backup layout contains a protected directory.");
                    replacements.Add(new(source, relative));
                }
            }
            else
            {
                string source = stageDir;
                // Legacy flat backups and one wrapped world are accepted; ambiguous nesting is rejected.
                if (!File.Exists(Path.Combine(source, "level.dat")) && metadata.Compatibility.Family != EngineFamily.Pocketmine)
                {
                    var candidates = Directory.GetFiles(stageDir, "level.dat", SearchOption.AllDirectories);
                    if (candidates.Length != 1) throw new InvalidDataException("Backup has no unambiguous world root.");
                    source = Path.GetDirectoryName(candidates[0])!;
                }
                if (!HasLevelDat(source)) throw new InvalidDataException("Backup is missing level.dat.");
                replacements.Add(new(source, Path.GetRelativePath(serverDir, worldDir)));
            }
            onProgress?.Invoke("Applying restored world...");
            InstanceFileTransaction.Commit(serverDir, replacements);
            onProgress?.Invoke("World restored successfully!");
        }
        finally { if (Directory.Exists(stageDir)) await TryCleanDirectoryAsync(stageDir, "restore staging directory"); }
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

    private void PruneOldBackups(string backupDirectory, int maxToKeep, Guid instanceId)
    {
        var files = new DirectoryInfo(backupDirectory)
            .GetFiles($"world-{instanceId:N}-*.zip")
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

