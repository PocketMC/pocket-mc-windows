namespace PocketMC.Domain.Models;

public enum CloudBackupConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Expired,
    Unauthorized,
    Error
}
