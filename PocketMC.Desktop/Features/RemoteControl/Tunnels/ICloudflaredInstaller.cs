namespace PocketMC.Desktop.Features.RemoteControl.Tunnels;

public interface ICloudflaredInstaller
{
    Task<string> EnsureInstalledAsync(CancellationToken cancellationToken);
}
