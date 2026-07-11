namespace PocketMC.RemoteControl.Tunnels;

public sealed class RemoteTunnelStartRequest
{
    public required int LocalPort { get; init; }
    public required string LocalUrl { get; init; }
}
