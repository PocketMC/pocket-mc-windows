using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PocketMC.Desktop.Tests;

internal sealed class MarketplaceTestHttpClientFactory : IHttpClientFactory
{
    private readonly Func<string, HttpClient> _factory;

    public MarketplaceTestHttpClientFactory(Func<HttpClient> factory)
        : this(_ => factory())
    {
    }

    public MarketplaceTestHttpClientFactory(Func<string, HttpClient> factory)
    {
        _factory = factory;
    }

    public HttpClient CreateClient(string name) => _factory(name);
}

internal sealed class MarketplaceDelegateHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

    public MarketplaceDelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request, cancellationToken));
    }
}

internal static class MarketplaceHttpResponses
{
    public static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Bytes(byte[] bytes)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
    }
}
