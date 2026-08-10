namespace PocketMC.RemoteControl.Models;

public class RemoteBackupDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = "Local"; // Local or Cloud
    public bool IsAutomated { get; set; }
}
