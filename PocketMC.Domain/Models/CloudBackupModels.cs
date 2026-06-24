using System;
using System.Threading;

namespace PocketMC.Domain.Models;

public class CloudBackupAccount
{
    public CloudBackupProviderType Provider { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? ConnectedAtUtc { get; set; }
    public DateTimeOffset? LastValidatedAtUtc { get; set; }
    public CloudBackupConnectionStatus Status { get; set; } = CloudBackupConnectionStatus.Disconnected;
    public string? ProviderAccountId { get; set; }
}

public class CloudBackupTarget
{
    public CloudBackupProviderType Provider { get; set; }
    public bool Enabled { get; set; }
    public string? RemoteRootPath { get; set; }
    public string? RemoteFolderId { get; set; }
    public bool UploadLatestOnly { get; set; }
    public int? RetentionCount { get; set; } = 3;
    public DateTimeOffset? LastUploadUtc { get; set; }
    public string? LastUploadFileName { get; set; }
    public long? LastUploadSizeBytes { get; set; }
    public string? LastUploadProviderFileId { get; set; }
    public string? LastError { get; set; }
}

public class CloudBackupUploadRequest
{
    public Guid InstanceId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string LocalZipPath { get; set; } = string.Empty;
    public string BackupFileName { get; set; } = string.Empty;
    public DateTimeOffset BackupCreatedUtc { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public IProgress<CloudBackupProgress>? Progress { get; set; }
}

public class CloudBackupUploadResult
{
    public bool Success { get; set; }
    public CloudBackupProviderType Provider { get; set; }
    public string? ProviderFileId { get; set; }
    public string? RemotePath { get; set; }
    public string? WebUrl { get; set; }
    public long BytesUploaded { get; set; }
    public string? Sha256 { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Recoverable { get; set; }
}

public class CloudBackupProgress
{
    public CloudBackupProviderType Provider { get; set; }
    public string Stage { get; set; } = string.Empty;
    public long BytesUploaded { get; set; }
    public long TotalBytes { get; set; }
    public double Percent { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CloudRemoteBackupItem
{
    public CloudBackupProviderType Provider { get; set; }
    public string ProviderFileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ModifiedUtc { get; set; }
    public string? WebUrl { get; set; }
}
