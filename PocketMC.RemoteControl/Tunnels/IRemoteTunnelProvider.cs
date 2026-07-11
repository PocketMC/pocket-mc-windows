namespace PocketMC.RemoteControl.Tunnels;

public interface IRemoteTunnelProvider
{
    string Id { get; }
    string DisplayName { get; }

    Task<RemoteTunnelStartResult> StartAsync(
        RemoteTunnelStartRequest request,
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    RemoteTunnelStatus GetStatus();
}
