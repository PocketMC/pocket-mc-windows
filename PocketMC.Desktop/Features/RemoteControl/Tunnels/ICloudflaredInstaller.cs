namespace PocketMC.Desktop.Features.RemoteControl.Tunnels;

public interface ICloudflaredInstaller
{
    Task<string> EnsureInstalledAsync(string? userConfiguredPath, CancellationToken cancellationToken);
}
