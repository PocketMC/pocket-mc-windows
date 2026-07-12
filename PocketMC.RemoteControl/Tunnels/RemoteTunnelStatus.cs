namespace PocketMC.RemoteControl.Tunnels;

public sealed class RemoteTunnelStatus
{
    public bool IsRunning { get; init; }
    public string? PublicUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
}
