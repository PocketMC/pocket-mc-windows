using System;
using System.Collections.Generic;


namespace PocketMC.Domain.Models;

public class CloudBackupSettings
{
    public bool EnableCloudBackups { get; set; } = false;
    public List<CloudBackupTarget> Targets { get; set; } = new();
    public bool UploadOnManualBackup { get; set; } = true;
    public bool UploadOnScheduledBackup { get; set; } = true;
    public int MaxConcurrentProviderUploads { get; set; } = 2;
    public bool KeepExternalBackupDirectoryFallback { get; set; } = true;
}

public class CloudOAuthTokenSet
{
    public CloudBackupProviderType Provider { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? Scope { get; set; }
    public string? TokenType { get; set; }
    public string? AccountId { get; set; }
}
