using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Desktop.Features.Instances.Backups;

/// <summary>
/// Represents the trigger reason for a backup creation.
/// </summary>
public enum BackupTrigger
{
    Manual,
    Scheduled
}

/// <summary>
/// Metadata stored alongside each backup ZIP to enable versioning, 
/// history tracking, and health diagnostics.
/// </summary>
public class BackupMetadataEntry
{
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public BackupTrigger Trigger { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    public long SizeBytes { get; set; }
    public string ServerType { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 checksum of the ZIP file for integrity verification.
    /// </summary>
    public string? Sha256Checksum { get; set; }

    /// <summary>
    /// Whether the integrity check has been verified since creation.
    /// </summary>
    public bool IntegrityVerified { get; set; }

    /// <summary>
    /// Size delta from the previous backup in bytes. Null for the first backup.
    /// </summary>
    public long? SizeDeltaBytes { get; set; }

    /// <summary>
    /// Sequential version number (1, 2, 3, ...) for this instance's backups.
    /// </summary>
    public int Version { get; set; }
}

/// <summary>
/// Persisted manifest for all backup metadata entries associated with
/// a single server instance. Stored as backups/backup-manifest.json.
/// </summary>
public class BackupManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<BackupMetadataEntry> Entries { get; set; } = new();

    /// <summary>
    /// Tracks the last failed scheduled backup time (if any) for health warnings.
    /// </summary>
    public DateTime? LastFailedBackupUtc { get; set; }

    /// <summary>
    /// Human-readable error from last failed backup.
    /// </summary>
    public string? LastFailureReason { get; set; }

    public static string GetManifestPath(string serverDir)
        => Path.Combine(serverDir, "backups", "backup-manifest.json");

    public static BackupManifest Load(string serverDir)
    {
        var path = GetManifestPath(serverDir);
        if (!File.Exists(path))
            return new BackupManifest();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions) ?? new BackupManifest();
        }
        catch
        {
            return new BackupManifest();
        }
    }

    public void Save(string serverDir)
    {
        var path = GetManifestPath(serverDir);
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        FileUtils.AtomicWriteAllText(path, json);
    }

    /// <summary>
    /// Get the next sequential version number.
    /// </summary>
    public int GetNextVersion()
    {
        if (Entries.Count == 0) return 1;
        return Entries.Max(e => e.Version) + 1;
    }

    /// <summary>
    /// Remove entries whose ZIP files no longer exist on disk (pruned or manually deleted).
    /// </summary>
    public void PurgeOrphanedEntries(string serverDir)
    {
        var backupDir = Path.Combine(serverDir, "backups");
        Entries.RemoveAll(e =>
        {
            string? zipPath = ResolveBackupFilePath(backupDir, e.FileName);
            return !File.Exists(zipPath);
        });
    }

    private static string? ResolveBackupFilePath(string backupDir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        return PathSafety.ValidateContainedPath(backupDir, fileName);
    }
}
