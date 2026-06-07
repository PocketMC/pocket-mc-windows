using System.IO;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.RemoteControl.Tunnels;

public sealed class CloudflaredInstaller : ICloudflaredInstaller
{
    private readonly ApplicationState _applicationState;
    private readonly DownloaderService _downloaderService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CloudflaredInstaller(ApplicationState applicationState, DownloaderService downloaderService)
    {
        _applicationState = applicationState;
        _downloaderService = downloaderService;
    }

    public async Task<string> EnsureInstalledAsync(string? userConfiguredPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(userConfiguredPath) && File.Exists(userConfiguredPath))
        {
            return userConfiguredPath;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string pathPart in pathEnv.Split(Path.PathSeparator))
            {
                string combined = Path.Combine(pathPart, "cloudflared.exe");
                if (File.Exists(combined))
                {
                    return combined;
                }
            }
        }

        if (!_applicationState.IsConfigured)
        {
            throw new InvalidOperationException("PocketMC must be configured before cloudflared can be downloaded.");
        }

        string appRootPath = _applicationState.GetRequiredAppRootPath();
        string cloudflaredPath = GetManagedExecutablePath(appRootPath);
        if (File.Exists(cloudflaredPath))
        {
            return cloudflaredPath;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(cloudflaredPath))
            {
                await _downloaderService.EnsureCloudflaredDownloadedAsync(appRootPath, progress: null, cancellationToken);
            }

            return cloudflaredPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string GetManagedExecutablePath(string appRootPath) =>
        Path.Combine(appRootPath, "tunnel", "cloudflared.exe");
}
