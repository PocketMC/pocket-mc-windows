using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Infrastructure.Storage;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances.Providers;

public sealed class BedrockBdsProviderTests
{
    [Fact]
    public async Task GetAvailableVersionsAsync_RetriesAfterATransientManifestFailure()
    {
        var handler = new FlakyManifestHandler("""
            {
              "release": { "1.21.90": { "windows": { "url": "https://example.test/bds.zip" } } },
              "preview": {}
            }
            """);
        var provider = new BedrockBdsProvider(
            new HttpClient(handler),
            new DownloaderService(new SingleClientFactory(handler), NullLogger<DownloaderService>.Instance),
            NullLogger<BedrockBdsProvider>.Instance);

        TimeSpan originalDelay = BedrockBdsProvider.ManifestRetryDelay;
        BedrockBdsProvider.ManifestRetryDelay = TimeSpan.Zero;
        try
        {
            List<MinecraftVersion> first = await provider.GetAvailableVersionsAsync();
            Assert.Single(first);
            Assert.Equal("latest", first[0].Id);

            List<MinecraftVersion> second = await provider.GetAvailableVersionsAsync();
            Assert.Single(second);
            Assert.Equal("1.21.90", second[0].Id);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            BedrockBdsProvider.ManifestRetryDelay = originalDelay;
        }
    }

    private sealed class FlakyManifestHandler : HttpMessageHandler
    {
        private readonly string _manifest;
        private int _requests;

        public FlakyManifestHandler(string manifest)
        {
            _manifest = manifest;
        }

        public int RequestCount => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref _requests);
            if (attempt == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_manifest, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
