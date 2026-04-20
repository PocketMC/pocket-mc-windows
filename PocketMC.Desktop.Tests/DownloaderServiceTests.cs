using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Tests;

public sealed class DownloaderServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "PocketMC.DownloaderTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadFileAsync_WithoutExpectedHash_DoesNotResumeExistingPartial()
    {
        Directory.CreateDirectory(_rootPath);
        string destinationPath = Path.Combine(_rootPath, "playit.exe");
        string partialPath = destinationPath + ".partial";
        await File.WriteAllTextAsync(partialPath, "stale-partial");

        RangeHeaderValue? observedRange = null;
        var service = new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((request, _) =>
            {
                observedRange = request.Headers.Range;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("fresh-binary"))
                };
            }))),
            NullLogger<DownloaderService>.Instance);

        await service.DownloadFileAsync("https://example.invalid/playit.exe", destinationPath, expectedHash: null);

        Assert.Null(observedRange);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal("fresh-binary", await File.ReadAllTextAsync(destinationPath));
    }

    [Fact]
    public async Task DownloadFileAsync_WithExpectedHash_ResumesExistingPartial()
    {
        Directory.CreateDirectory(_rootPath);
        string destinationPath = Path.Combine(_rootPath, "runtime.zip");
        string partialPath = destinationPath + ".partial";
        await File.WriteAllTextAsync(partialPath, "abc");

        string expectedContent = "abcdef";
        string expectedHash = ComputeSha256Hex(expectedContent);
        RangeHeaderValue? observedRange = null;

        var service = new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((request, _) =>
            {
                observedRange = request.Headers.Range;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("def"))
                };

                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
                return response;
            }))),
            NullLogger<DownloaderService>.Instance);

        await service.DownloadFileAsync("https://example.invalid/runtime.zip", destinationPath, expectedHash);

        Assert.NotNull(observedRange);
        Assert.Equal(3, observedRange!.Ranges.Single().From);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal(expectedContent, await File.ReadAllTextAsync(destinationPath));
    }

    [Fact]
    public void CanResumePlayitDownload_WhenHashIsUnpinned_ReturnsFalse()
    {
        var service = new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                }))),
            NullLogger<DownloaderService>.Instance);

        Assert.False(service.CanResumePlayitDownload());
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static string ComputeSha256Hex(string content)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
