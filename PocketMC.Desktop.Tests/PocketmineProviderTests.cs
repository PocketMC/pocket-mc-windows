using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Tests;

public sealed class PocketmineProviderTests
{
    [Fact]
    public async Task GetAvailableVersionsAsync_UsesCachedReleases()
    {
        int requestCount = 0;
        using var httpClient = new HttpClient(new DelegateHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"tag_name":"5.0.0","prerelease":false,"assets":[{"name":"PocketMine-MP.phar","browser_download_url":"https://example.invalid/PocketMine-MP.phar"}]}]""",
                    Encoding.UTF8,
                    "application/json")
            };
        }));

        var downloader = new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) }))),
            NullLogger<DownloaderService>.Instance);

        var provider = new PocketmineProvider(httpClient, downloader, NullLogger<PocketmineProvider>.Instance);

        var first = await provider.GetAvailableVersionsAsync();
        var second = await provider.GetAvailableVersionsAsync();

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task DownloadSoftwareAsync_WhenRateLimited_ThrowsFriendlyMessage()
    {
        using var httpClient = new HttpClient(new DelegateHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"message":"API rate limit exceeded"}""", Encoding.UTF8, "application/json")
            };

            long reset = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.ToString());
            return response;
        }));

        var downloader = new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) }))),
            NullLogger<DownloaderService>.Instance);

        var provider = new PocketmineProvider(httpClient, downloader, NullLogger<PocketmineProvider>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.DownloadSoftwareAsync("5.0.0", Path.Combine(Path.GetTempPath(), "pmmp-test.phar")));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Try again", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
