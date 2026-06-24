using PocketMC.Domain.Models;
namespace PocketMC.Desktop.Features.RemoteControl.Models;

public sealed class RemoteDashboardStatus
{
    public bool Enabled { get; set; }
    public bool HostRunning { get; set; }
    public int Port { get; set; }
    public RemoteAccessMode AccessMode { get; set; }
    public IReadOnlyList<string> LocalUrls { get; set; } = Array.Empty<string>();
    public string? PublicUrl { get; set; }
    public bool TunnelRunning { get; set; }
    public string? TunnelError { get; set; }
    public int ActiveDeviceCount { get; set; }
    public bool AllowRemoteConsoleCommands { get; set; }
    public bool AllowRemotePlayerActions { get; set; }
}
