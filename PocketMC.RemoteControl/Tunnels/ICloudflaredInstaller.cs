namespace PocketMC.RemoteControl.Tunnels;

public interface ICloudflaredInstaller
{
    Task<string> EnsureInstalledAsync(CancellationToken cancellationToken);
}
