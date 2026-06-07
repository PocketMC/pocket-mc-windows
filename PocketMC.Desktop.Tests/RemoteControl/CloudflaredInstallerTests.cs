using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class CloudflaredInstallerTests
{
    [Fact]
    public async Task EnsureCloudflaredDownloadedAsync_DownloadsWindowsExecutableToTunnelFolder()
    {
        var requestedUris = new List<Uri>();
        using var workspace = new PortReliabilityTestWorkspace();
        var downloader = new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((request, _) =>
            {
                requestedUris.Add(request.RequestUri!);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 0x4d, 0x5a, 0x90, 0x00 })
                };
            }))),
            NullLogger<DownloaderService>.Instance);

        await downloader.EnsureCloudflaredDownloadedAsync(workspace.RootPath);

        string cloudflaredPath = Path.Combine(workspace.RootPath, "tunnel", "cloudflared.exe");
        Assert.True(File.Exists(cloudflaredPath));
        Assert.Contains(
            requestedUris,
            uri => uri.ToString().Contains("cloudflared-windows-amd64.exe", StringComparison.OrdinalIgnoreCase));
    }
}
