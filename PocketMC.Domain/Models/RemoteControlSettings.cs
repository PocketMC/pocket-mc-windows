namespace PocketMC.Domain.Models;

public sealed class RemoteControlSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 25580;
    public RemoteAccessMode AccessMode { get; set; } = RemoteAccessMode.CloudflaredQuickTunnel;
    public string TunnelProviderId { get; set; } = "cloudflared-quick";
    public bool AllowRemoteConsoleCommands { get; set; }
    public bool AllowRemotePlayerActions { get; set; } = true;
    public string? PlayitTunnelId { get; set; }
    public string? PasswordHash { get; set; }
    public bool RequireAuthentication { get; set; } = true;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();
    public string? ProtectedPassword { get; set; }
}
