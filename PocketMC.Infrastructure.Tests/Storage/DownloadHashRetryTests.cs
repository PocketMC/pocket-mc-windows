using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

[Collection(DownloadTuningCollection.Name)]
public sealed class DownloadHashRetryTests
{
    [Fact]
    public async Task DownloadFile_Retries_WhenAMirrorReturnsCorruptedBytes()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('g', 4096));
        byte[] corrupt = (byte[])payload.Clone();
        corrupt[17] ^= 0xFF;

        string destination = Path.Combine(Path.GetTempPath(), $"pocketmc-hash-{Guid.NewGuid():N}.bin");
        var handler = new CorruptThenValidHandler(corrupt, payload);
        var service = new DownloaderService(new SingleClientFactory(handler), NullLogger<DownloaderService>.Instance);

        try
        {
            string expectedHash = Convert.ToHexString(SHA256.HashData(payload));
            await service.DownloadFileAsync("https://example.test/file.bin", destination, expectedHash, "sha256");

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".partial.identity"));
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
            if (File.Exists(destination + ".partial")) File.Delete(destination + ".partial");
            if (File.Exists(destination + ".partial.identity")) File.Delete(destination + ".partial.identity");
        }
    }

    private sealed class CorruptThenValidHandler : HttpMessageHandler
    {
        private readonly byte[] _corrupt;
        private readonly byte[] _payload;
        private int _calls;

        public CorruptThenValidHandler(byte[] corrupt, byte[] payload)
        {
            _corrupt = corrupt;
            _payload = payload;
        }

        public int RequestCount => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            byte[] body = call == 1 ? _corrupt : _payload;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = request
            };
            response.Content.Headers.ContentLength = body.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
