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
        if (!string.IsNullOrWhiteSpace(userConfiguredPath))
        {
            if (File.Exists(userConfiguredPath))
            {
                return userConfiguredPath;
            }
            throw new FileNotFoundException("The user-configured Cloudflared path does not exist.");
        }

        if (!_applicationState.IsConfigured)
        {
            throw new InvalidOperationException("PocketMC must be configured before cloudflared can be downloaded.");
        }

        string appRootPath = _applicationState.GetRequiredAppRootPath();
        string cloudflaredPath = GetManagedExecutablePath(appRootPath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cloudflaredPath))
            {
                if (DownloaderService.CloudflaredExpectedSha256 != null)
                {
                    bool isValid = await VerifySha256Async(cloudflaredPath, DownloaderService.CloudflaredExpectedSha256, cancellationToken);
                    if (!isValid)
                    {
                        File.Delete(cloudflaredPath);
                    }
                }
            }

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

    private static async Task<bool> VerifySha256Async(string filePath, string expectedHash, CancellationToken cancellationToken)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        return string.Equals(actualHash, expectedHash.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    public static string GetManagedExecutablePath(string appRootPath) =>
        Path.Combine(appRootPath, "tunnel", "cloudflared.exe");
}
