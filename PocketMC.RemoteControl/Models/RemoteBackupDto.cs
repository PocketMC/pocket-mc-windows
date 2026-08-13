namespace PocketMC.RemoteControl.Models;

public class RemoteBackupDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = "Local"; // Local or Cloud
    public bool IsAutomated { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ServerVersion { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public bool HasChecksum { get; set; }
    public bool IntegrityVerified { get; set; }
    public long? SizeDeltaBytes { get; set; }
    public string TriggerText { get; set; } = string.Empty;
}
