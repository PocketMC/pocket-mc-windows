using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

[Collection(DownloadTuningCollection.Name)]
public sealed class DownloadHashSpecTests
{
    [Fact]
    public async Task DownloadFile_RejectsAnUnknownDeclaredHashType()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.DownloadFileAsync("https://example.test/a.bin", TempPath(), new string('0', 32), "md5"));
    }

    [Fact]
    public async Task DownloadFile_RejectsADigestWhoseLengthDoesNotMatchItsDeclaredType()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DownloadFileAsync("https://example.test/a.bin", TempPath(), new string('0', 40), "sha256"));
    }

    [Fact]
    public async Task DownloadFile_NormalisesDasherisedHashNamesBeforeCheckingLength()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DownloadFileAsync("https://example.test/a.bin", TempPath(), new string('0', 40), "SHA-256"));
    }

    private static string TempPath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pocketmc-hashspec-{Guid.NewGuid():N}.bin");

    private static DownloaderService CreateService() =>
        new(new NoopClientFactory(), NullLogger<DownloaderService>.Instance);

    private sealed class NoopClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new InvalidOperationException("The network must not be touched for an invalid digest spec.");
        }
    }
}
