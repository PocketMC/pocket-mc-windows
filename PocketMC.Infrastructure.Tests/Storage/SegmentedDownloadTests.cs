using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

public sealed class DownloadTuningTests
{
    [Fact]
    public void Configure_ClampsThreadsToSupportedRange()
    {
        DownloadTuning.Configure(0, 0);
        Assert.Equal(DownloadTuning.MinThreads, DownloadTuning.Threads);
        Assert.Equal(DownloadTuning.MinConcurrency, DownloadTuning.Concurrency);

        DownloadTuning.Configure(5000, 99);
        Assert.Equal(DownloadTuning.MaxThreads, DownloadTuning.Threads);
        Assert.Equal(DownloadTuning.MaxConcurrency, DownloadTuning.Concurrency);

        DownloadTuning.Configure(8, 3);
    }

    [Fact]
    public void Apply_UsesSettingsValues()
    {
        DownloadTuning.Apply(new AppSettings { DownloadThreads = 32, DownloadConcurrency = 4 });
        Assert.Equal(32, DownloadTuning.Threads);
        Assert.Equal(4, DownloadTuning.Concurrency);

        DownloadTuning.Apply(new AppSettings());
        Assert.Equal(DownloadTuning.DefaultThreads, DownloadTuning.Threads);
        Assert.Equal(DownloadTuning.DefaultConcurrency, DownloadTuning.Concurrency);
    }

    [Fact]
    public void Apply_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DownloadTuning.Apply(null!));
    }
}

[Collection(DownloadTuningCollection.Name)]
public sealed class SegmentedDownloadTests
{
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SocketsHttpHandler { UseProxy = false });
    }

    [Fact]
    public async Task SegmentedDownload_WritesIdenticalContent_AndUsesRanges()
    {
        byte[] payload = CreatePayload(2 * 1024 * 1024);
        await using var server = new RangeTestServer(payload, supportsRanges: true, probeETag: "\"v1\"", segmentETag: "\"v1\"");
        string destination = TempPath();

        DownloadTuning.Configure(8, 1);
        try
        {
            var progress = new Progress<DownloadProgress>(_ => { });
            var downloader = new DownloaderService(new SingleClientFactory(), NullLogger<DownloaderService>.Instance);
            await downloader.DownloadFileAsync(new Uri(server.BaseUri, "file.bin").ToString(), destination, progress: progress);

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.True(server.RangeRequests >= 2, $"expected ranged requests, saw {server.RangeRequests}");
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            TryDelete(destination);
        }
    }

    [Fact]
    public async Task SegmentedDownload_FallsBack_WhenServerIgnoresRanges()
    {
        byte[] payload = CreatePayload(2 * 1024 * 1024);
        await using var server = new RangeTestServer(payload, supportsRanges: false);
        string destination = TempPath();

        DownloadTuning.Configure(8, 1);
        try
        {
            var downloader = new DownloaderService(new SingleClientFactory(), NullLogger<DownloaderService>.Instance);
            await downloader.DownloadFileAsync(new Uri(server.BaseUri, "file.bin").ToString(), destination);

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            TryDelete(destination);
        }
    }

    [Fact]
    public async Task SegmentedDownload_FallsBack_WhenMirrorServesADifferentObject()
    {
        byte[] payload = CreatePayload(2 * 1024 * 1024);
        await using var server = new RangeTestServer(payload, supportsRanges: true, probeETag: "\"v1\"", segmentETag: "\"v2\"");
        string destination = TempPath();

        DownloadTuning.Configure(4, 1);
        try
        {
            var downloader = new DownloaderService(new SingleClientFactory(), NullLogger<DownloaderService>.Instance);
            await downloader.DownloadFileAsync(new Uri(server.BaseUri, "file.bin").ToString(), destination);

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.True(server.RangeRequests >= 1, "expected the segmented path to be attempted first");
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            TryDelete(destination);
        }
    }

    [Fact]
    public async Task SegmentedDownload_FallsBack_WhenTheServerExposesNoValidator()
    {
        byte[] payload = CreatePayload(2 * 1024 * 1024);
        await using var server = new RangeTestServer(payload, supportsRanges: true);
        string destination = TempPath();

        DownloadTuning.Configure(4, 1);
        try
        {
            var downloader = new DownloaderService(new SingleClientFactory(), NullLogger<DownloaderService>.Instance);
            await downloader.DownloadFileAsync(new Uri(server.BaseUri, "file.bin").ToString(), destination);

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(0, server.RangeRequests);
            // One probe request plus the fallback single-stream transfer.
            Assert.Equal(2, server.FullRequests);
        }
        finally
        {
            DownloadTuning.Configure(DownloadTuning.DefaultThreads, DownloadTuning.DefaultConcurrency);
            TryDelete(destination);
        }
    }

    private static byte[] CreatePayload(int length)
    {
        byte[] payload = new byte[length];
        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index * 31 % 251);
        }

        return payload;
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"pocketmc-seg-{Guid.NewGuid():N}.bin");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".partial")) File.Delete(path + ".partial");
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Minimal HTTP/1.1 server that honours <c>Range</c> so tests need no URL ACL.</summary>
    private sealed class RangeTestServer : IAsyncDisposable
    {
        private readonly byte[] _payload;
        private readonly bool _supportsRanges;
        private readonly string? _probeETag;
        private readonly string? _segmentETag;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;
        private int _rangeRequests;
        private int _fullRequests;

        public RangeTestServer(byte[] payload, bool supportsRanges, string? probeETag = null, string? segmentETag = null)
        {
            _payload = payload;
            _supportsRanges = supportsRanges;
            _probeETag = probeETag;
            _segmentETag = segmentETag;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUri = new Uri($"http://127.0.0.1:{port}/");
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public Uri BaseUri { get; }

        public int RangeRequests => Volatile.Read(ref _rangeRequests);

        public int FullRequests => Volatile.Read(ref _fullRequests);

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using NetworkStream stream = client.GetStream();
                    string? rangeHeader = await ReadRangeHeaderAsync(stream);
                    (long start, long end)? range = ParseRange(rangeHeader, _payload.Length);

                    if (_supportsRanges && range is { } r)
                    {
                        Interlocked.Increment(ref _rangeRequests);
                        long length = r.end - r.start + 1;
                        string etag = _segmentETag is null ? "" : $"ETag: {_segmentETag}\r\n";
                        string head = $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {r.start}-{r.end}/{_payload.Length}\r\n" +
                                      $"Content-Length: {length}\r\n{etag}Accept-Ranges: bytes\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                        // A stale mirror copy differs in content, so mixing it into the file must be detectable.
                        bool differentObject = _segmentETag is not null && _segmentETag != _probeETag;
                        byte[] body = differentObject ? CorruptCopy(_payload) : _payload;
                        await stream.WriteAsync(body.AsMemory((int)r.start, (int)length));
                    }
                    else
                    {
                        Interlocked.Increment(ref _fullRequests);
                        string etag = _probeETag is null ? "" : $"ETag: {_probeETag}\r\n";
                        string head = $"HTTP/1.1 200 OK\r\nContent-Length: {_payload.Length}\r\n{etag}Accept-Ranges: bytes\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                        await stream.WriteAsync(_payload);
                    }
                }
                catch (Exception)
                {
                    // Aborted request: the client already has what it needs.
                }
            }
        }

        private static byte[] CorruptCopy(byte[] source)
        {
            byte[] copy = (byte[])source.Clone();
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = (byte)(copy[index] ^ 0xFF);
            }

            return copy;
        }

        private static async Task<string?> ReadRangeHeaderAsync(NetworkStream stream)
        {
            var sb = new StringBuilder();
            byte[] buffer = new byte[1];
            string? rangeHeader = null;

            while (sb.Length < 8192)
            {
                int read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                sb.Append((char)buffer[0]);
                if (sb.Length >= 4 && sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
            }

            foreach (string line in sb.ToString().Split("\r\n"))
            {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                {
                    rangeHeader = line["Range:".Length..].Trim();
                }
            }

            return rangeHeader;
        }

        private static (long start, long end)? ParseRange(string? header, int total)
        {
            if (header == null || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
            string[] bounds = header["bytes=".Length..].Split('-');
            if (bounds.Length != 2 || !long.TryParse(bounds[0], out long start)) return null;
            long end = long.TryParse(bounds[1], out long parsedEnd) && bounds[1].Length > 0 ? parsedEnd : total - 1;
            return (start, Math.Min(end, total - 1));
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                await _acceptLoop;
            }
            catch (Exception)
            {
            }

            _cts.Dispose();
        }
    }
}
