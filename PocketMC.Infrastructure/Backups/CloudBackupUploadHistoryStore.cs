using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Storage;

namespace PocketMC.Infrastructure.Backups;

public class UploadHistoryRecord
{
    public Guid InstanceId { get; set; }
    public string LocalBackupFileName { get; set; } = string.Empty;
    public string? LocalBackupSha256 { get; set; }
    public CloudBackupProviderType Provider { get; set; }
    public string? ProviderFileId { get; set; }
    public string? RemotePath { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public long SizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class CloudBackupUploadHistoryStore
{
    private readonly string _historyFilePath;
    private readonly ILogger<CloudBackupUploadHistoryStore> _logger;
    private readonly object _lock = new();

    public CloudBackupUploadHistoryStore(ILogger<CloudBackupUploadHistoryStore> logger)
    {
        _logger = logger;
        _historyFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PocketMC",
            "cloud-backup-history.json");
    }

    public List<UploadHistoryRecord> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_historyFilePath))
            {
                return new List<UploadHistoryRecord>();
            }

            try
            {
                string json = File.ReadAllText(_historyFilePath);
                var records = JsonSerializer.Deserialize<List<UploadHistoryRecord>>(json);
                return records ?? new List<UploadHistoryRecord>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cloud backup history.");
                return new List<UploadHistoryRecord>();
            }
        }
    }

    public void Save(List<UploadHistoryRecord> records)
    {
        lock (_lock)
        {
            try
            {
                string dir = Path.GetDirectoryName(_historyFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                FileUtils.AtomicWriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cloud backup history.");
            }
        }
    }

    public void AddRecord(UploadHistoryRecord record)
    {
        var records = Load();
        records.Add(record);
        Save(records);
    }
}
