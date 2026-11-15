using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

public sealed class DownloadMirrorHandlerTests
{
    [Fact]
    public async Task SendAsync_WhenMirrorReturnsNotFound_FallsBackToOrigin()
    {
        const string origin = "https://github.com/owner/repo/releases/download/v1/server.jar";
        var inner = new StubHandler(uri => uri.Host == "mirror.example"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Ok());
        using var handler = CreateHandler(inner);

        HttpResponseMessage response = await SendAsync(handler, origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.RequestedUris.Count);
        Assert.Equal(origin, inner.RequestedUris[1].AbsoluteUri);
    }

    [Fact]
    public async Task SendAsync_WhenMirrorReturnsForbidden_FallsBackToOrigin()
    {
        const string origin = "https://github.com/owner/repo/releases/download/v1/server.jar";
        var inner = new StubHandler(uri => uri.Host == "mirror.example"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Ok());
        using var handler = CreateHandler(inner);

        HttpResponseMessage response = await SendAsync(handler, origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, inner.RequestedUris[1].AbsoluteUri);
    }

    [Fact]
    public async Task SendAsync_WhenOriginReturnsNotFound_ReturnsNotFoundWithoutThrowing()
    {
        const string origin = "https://github.com/owner/repo/releases/download/v1/missing.jar";
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var handler = CreateHandler(inner);

        HttpResponseMessage response = await SendAsync(handler, origin);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(origin, inner.RequestedUris.Last().AbsoluteUri);
    }

    [Fact]
    public async Task SendAsync_StopsTryingMirrorsAfterTheAttemptBudget()
    {
        const string origin = "https://github.com/owner/repo/releases/download/v1/server.jar";
        var inner = new StubHandler(uri => uri.Host == "github.com"
            ? Ok()
            : new HttpResponseMessage(HttpStatusCode.BadGateway));
        var options = new DownloadMirrorOptions(
            true,
            "https://mirror.example/{url}",
            new[]
            {
                "https://m1.example/{url}", "https://m2.example/{url}", "https://m3.example/{url}",
                "https://m4.example/{url}", "https://m5.example/{url}", "https://m6.example/{url}"
            });
        var mirrors = new DownloadMirrorService(() => options);
        using var handler = new DownloadMirrorHandler(mirrors, NullLogger<DownloadMirrorHandler>.Instance)
        {
            InnerHandler = inner
        };

        HttpResponseMessage response = await SendAsync(handler, origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // custom mirror + jsDelivr + two tunnels hit the cap, then the origin is requested directly.
        Assert.True(inner.RequestedUris.Count <= 5, $"Unexpected attempts: {inner.RequestedUris.Count}");
        Assert.Equal(origin, inner.RequestedUris.Last().AbsoluteUri);
        Assert.DoesNotContain(inner.RequestedUris, uri => uri.Host is "m4.example" or "m5.example" or "m6.example");
    }

    private static DownloadMirrorHandler CreateHandler(HttpMessageHandler inner)
    {
        var options = new DownloadMirrorOptions(true, "https://mirror.example/{url}", Array.Empty<string>());
        var mirrors = new DownloadMirrorService(() => options);
        return new DownloadMirrorHandler(mirrors, NullLogger<DownloadMirrorHandler>.Instance)
        {
            InnerHandler = inner
        };
    }

    [Fact]
    public async Task SendAsync_DoesNotForwardCredentialsToMirrors()
    {
        const string origin = "https://github.com/owner/repo/releases/download/v1/server.jar";
        var inner = new StubHandler(uri => uri.Host == "mirror.example"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Ok());
        using var handler = CreateHandler(inner);

        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, origin);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");
        request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(inner.RequestedHeaders[0]!.Authorization);
        Assert.NotNull(inner.RequestedHeaders[0]!.Accept);
    }

    private static async Task<HttpResponseMessage> SendAsync(DownloadMirrorHandler handler, string url)
    {
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await invoker.SendAsync(request, CancellationToken.None);
    }

    private static HttpResponseMessage Ok()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("payload")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> _responder;

        public StubHandler(Func<Uri, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<Uri> RequestedUris { get; } = new();

        public List<System.Net.Http.Headers.HttpRequestHeaders> RequestedHeaders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            RequestedUris.Add(uri);
            RequestedHeaders.Add(request.Headers);

            HttpResponseMessage response = _responder(uri);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
