using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Infrastructure.Instances.Providers;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances.Providers;

public sealed class ProviderMetaCacheTests
{
    [Fact]
    public async Task GetRawAsync_ServesTheCachedPayloadUntilCleared()
    {
        var handler = new CountingHandler();
        using var client = new HttpClient(handler, disposeHandler: false);
        const string url = "https://example.test/index.json";

        ProviderMetaCache.Clear();
        try
        {
            await ProviderMetaCache.GetRawAsync(client, url);
            await ProviderMetaCache.GetRawAsync(client, url);
            Assert.Equal(1, handler.RequestCount);

            ProviderMetaCache.Clear();

            await ProviderMetaCache.GetRawAsync(client, url);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            ProviderMetaCache.Clear();
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
