using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Tunnel;

namespace PocketMC.Infrastructure.Tests.Tunnel;

public sealed class PlayitRuntimeManagementTests
{
    [Theory]
    [InlineData("1.0.8", false)]
    [InlineData("1.0.9", false)]
    [InlineData("1.0.10", true)]
    [InlineData("1.1.0", true)]
    public void IsCompatibleVersion_AppliesMinimumCompatibilityPolicy(string value, bool expected)
    {
        Assert.Equal(expected, PlayitRuntimeManifest.IsCompatibleVersion(Version.Parse(value)));
    }

    [Fact]
    public async Task EnsurePlayitDownloadedAsync_FailedDownloadPreservesExistingBinaryAndDoesNotStopProcess()
    {
        string appRoot = Path.Combine(Path.GetTempPath(), "PocketMC-Playit-Test", Guid.NewGuid().ToString("N"));
        string executablePath = Path.Combine(appRoot, "tunnel", PlayitRuntimeManifest.ExecutableName);
        byte[] existingBinary = { 0x4d, 0x5a, 0x01, 0x02 };
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        await File.WriteAllBytesAsync(executablePath, existingBinary);

        try
        {
            using var handler = new StaticResponseHandler(HttpStatusCode.NotFound);
            using var client = new HttpClient(handler);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(value => value.CreateClient("PocketMC.Downloads")).Returns(client);
            var downloader = new DownloaderService(factory.Object, NullLogger<DownloaderService>.Instance);
            bool processStopCallbackCalled = false;

            await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.EnsurePlayitDownloadedAsync(
                appRoot,
                cancellationToken: CancellationToken.None,
                beforeReplace: _ =>
                {
                    processStopCallbackCalled = true;
                    return Task.CompletedTask;
                }));

            Assert.Equal(existingBinary, await File.ReadAllBytesAsync(executablePath));
            Assert.False(processStopCallbackCalled);
        }
        finally
        {
            if (Directory.Exists(appRoot))
            {
                Directory.Delete(appRoot, recursive: true);
            }
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}