using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

[Collection(DownloadTuningCollection.Name)]
public sealed class DownloadDestinationLockTests
{
    [Fact]
    public async Task ConcurrentDownloads_ToSameDestination_AreSerialised()
    {
        string destination = Path.Combine(Path.GetTempPath(), $"pocketmc-lock-{Guid.NewGuid():N}.bin");
        var handler = new GatedHandler(Encoding.UTF8.GetBytes(new string('x', 8192)));
        var service = new DownloaderService(new SingleClientFactory(handler), NullLogger<DownloaderService>.Instance);

        DownloadTuning.Configure(1, 1);
        try
        {
            Task first = service.DownloadFileAsync("https://example.test/file.bin", destination);
            await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task second = service.DownloadFileAsync("https://example.test/file.bin", destination);
            await Task.Delay(300);

            // The second download must wait for the first to finish instead of sharing the .partial file.
            Assert.Equal(1, handler.RequestCount);

            handler.Release();
            await Task.WhenAll(first, second);

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(8192, new FileInfo(destination).Length);
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            TryDelete(destination);
            TryDelete(destination + ".partial");
            TryDelete(destination + ".partial.identity");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
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

    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedHandler(byte[] payload)
        {
            _payload = payload;
        }

        public TaskCompletionSource FirstRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount { get; private set; }

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int count = Interlocked.Increment(ref _requestCount);
            RequestCount = count;
            if (count == 1)
            {
                FirstRequestStarted.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(_payload)
            };
        }

        private int _requestCount;
    }
}
