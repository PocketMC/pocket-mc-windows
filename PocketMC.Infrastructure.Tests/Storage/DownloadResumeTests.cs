using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

[Collection(DownloadTuningCollection.Name)]
public sealed class DownloadResumeTests
{
    [Fact]
    public async Task DownloadFile_RestartsFromScratch_WhenServerReturnsMismatchedRange()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('a', 4000));
        string destination = Path.Combine(Path.GetTempPath(), $"pocketmc-resume-{Guid.NewGuid():N}.bin");
        string partial = destination + ".partial";
        string identity = partial + ".identity";
        await File.WriteAllBytesAsync(partial, payload[..20]);
        await File.WriteAllTextAsync(identity, "https://example.test/file.bin");

        var handler = new BadRangeThenFullHandler(payload);
        var service = new DownloaderService(
            new SingleClientFactory(handler), NullLogger<DownloaderService>.Instance);

        DownloadTuning.Configure(1, 1);
        try
        {
            string expectedHash = Convert.ToHexString(SHA256.HashData(payload));
            await service.DownloadFileAsync(
                "https://example.test/file.bin", destination, expectedHash, "sha256");

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            foreach (string path in new[] { destination, partial, identity })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task DownloadFile_SendsIfRange_WhenResumingWithKnownValidator()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('b', 3000));
        string destination = Path.Combine(Path.GetTempPath(), $"pocketmc-ifrange-{Guid.NewGuid():N}.bin");
        string partial = destination + ".partial";
        string identity = partial + ".identity";
        await File.WriteAllBytesAsync(partial, payload[..20]);
        await File.WriteAllTextAsync(
            identity,
            $"{{\"Url\":\"https://example.test/file.bin\",\"ETag\":\"\\\"v1\\\"\",\"LastModified\":null,\"Length\":{payload.Length}}}");

        var handler = new ReplacedObjectHandler(payload);
        var service = new DownloaderService(
            new SingleClientFactory(handler), NullLogger<DownloaderService>.Instance);

        DownloadTuning.Configure(1, 1);
        try
        {
            string expectedHash = Convert.ToHexString(SHA256.HashData(payload));
            await service.DownloadFileAsync(
                "https://example.test/file.bin", destination, expectedHash, "sha256");

            Assert.True(handler.SawIfRange);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            foreach (string path in new[] { destination, partial, identity })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }


    [Fact]
    public async Task DownloadFile_RefreshesIdentityAfterARestart()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('c', 4000));
        string destination = Path.Combine(Path.GetTempPath(), $"pocketmc-identity-{Guid.NewGuid():N}.bin");
        string partial = destination + ".partial";
        string identity = partial + ".identity";
        await File.WriteAllBytesAsync(partial, payload[..20]);
        await File.WriteAllTextAsync(
            identity,
            $"{{\"Url\":\"https://example.test/file.bin\",\"ETag\":\"\\\"v1\\\"\",\"LastModified\":null,\"Length\":20}}");

        var handler = new RestartWithNewObjectHandler(payload);
        var service = new DownloaderService(
            new SingleClientFactory(handler), NullLogger<DownloaderService>.Instance);

        DownloadTuning.Configure(1, 1);
        using var cts = new CancellationTokenSource();
        try
        {
            // Stop right after the restart body starts so the refreshed identity stays on disk.
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (value.BytesRead > 0)
                {
                    cts.Cancel();
                }
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadFileAsync(
                "https://example.test/file.bin", destination, progress: progress, cancellationToken: cts.Token));

            string stored = await File.ReadAllTextAsync(identity);
            Assert.Contains("v2", stored);
            Assert.Contains($"\"Length\":{payload.Length}", stored);
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            foreach (string path in new[] { destination, partial, identity })
            {
                if (File.Exists(path)) File.Delete(path);
            }
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

    private sealed class BadRangeThenFullHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public BadRangeThenFullHandler(byte[] payload)
        {
            _payload = payload;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (request.Headers.Range != null)
            {
                var rangeContent = new ByteArrayContent(new byte[10]);
                rangeContent.Headers.ContentRange = new ContentRangeHeaderValue(0, 9, _payload.Length);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    RequestMessage = request,
                    Content = rangeContent
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(_payload)
            });
        }
    }

    private sealed class ReplacedObjectHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public ReplacedObjectHandler(byte[] payload)
        {
            _payload = payload;
        }

        public bool SawIfRange { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Range != null && request.Headers.IfRange != null)
            {
                SawIfRange = true;
            }

            // The object behind the URL changed, so the range condition fails and a full body is returned.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(_payload)
            });
        }
    }

    private sealed class RestartWithNewObjectHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public RestartWithNewObjectHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Range != null)
            {
                var rangeContent = new ByteArrayContent(new byte[10]);
                rangeContent.Headers.ContentRange = new ContentRangeHeaderValue(0, 9, _payload.Length);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    RequestMessage = request,
                    Content = rangeContent
                });
            }

            var full = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(_payload)
            };
            full.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return Task.FromResult(full);
        }
    }
}
