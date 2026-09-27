using System.Collections.Generic;
using System.Text.Json;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.AI;
using Xunit;

namespace PocketMC.Infrastructure.Tests.AI;

public class OllamaServiceTests
{
    // ── URL Normalization ───────────────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434")]
    [InlineData("http://localhost:11434/", "http://localhost:11434")]
    [InlineData("http://localhost:11434/api/chat", "http://localhost:11434")]
    [InlineData("http://localhost:11434/api/tags", "http://localhost:11434")]
    [InlineData("http://localhost:11434/api/", "http://localhost:11434")]
    [InlineData("http://localhost:11434/api", "http://localhost:11434")]
    [InlineData("localhost:11434", "http://localhost:11434")]
    [InlineData("", "http://localhost:11434")]
    [InlineData("   ", "http://localhost:11434")]
    [InlineData("https://ollama.com", "https://ollama.com")]
    [InlineData("https://ollama.com/api/chat", "https://ollama.com")]
    [InlineData("ollama.com", "https://ollama.com")]
    [InlineData("http://192.168.1.50:11434", "http://192.168.1.50:11434")]
    [InlineData("http://192.168.1.50:11434/api/chat", "http://192.168.1.50:11434")]
    [InlineData("192.168.1.50:11434", "http://192.168.1.50:11434")]
    public void NormalizeBaseUrl_HandlesVariousInputs(string input, string expected)
    {
        var result = OllamaService.NormalizeBaseUrl(input);
        Assert.Equal(expected, result);
    }

    // ── Cloud Endpoint Detection ────────────────────────────────────────

    [Theory]
    [InlineData("https://ollama.com", true)]
    [InlineData("https://OLLAMA.COM", true)]
    [InlineData("http://localhost:11434", false)]
    [InlineData("http://192.168.1.50:11434", false)]
    public void IsCloudEndpoint_DetectsCorrectly(string url, bool expected)
    {
        Assert.Equal(expected, OllamaService.IsCloudEndpoint(url));
    }

    // ── Parse Models From Tags Response ─────────────────────────────────

    [Fact]
    public void ParseModelsFromTagsResponse_ParsesValidResponse()
    {
        var json = """
        {
          "models": [
            {
              "name": "qwen3:8b",
              "model": "qwen3:8b",
              "size": 5225388164,
              "details": {
                "parameter_size": "8.2B",
                "quantization_level": "Q4_K_M",
                "family": "qwen3"
              },
              "capabilities": ["completion", "tools", "thinking"]
            },
            {
              "name": "nomic-embed-text:latest",
              "model": "nomic-embed-text:latest",
              "size": 274302450,
              "details": {
                "parameter_size": "137M",
                "quantization_level": "F16",
                "family": "nomic-bert"
              },
              "capabilities": ["embedding"]
            }
          ]
        }
        """;

        var models = OllamaService.ParseModelsFromTagsResponse(json);

        Assert.Equal(2, models.Count);

        Assert.Equal("qwen3:8b", models[0].Name);
        Assert.Equal("8.2B", models[0].ParameterSize);
        Assert.Equal("Q4_K_M", models[0].QuantizationLevel);
        Assert.Equal("qwen3", models[0].Family);
        Assert.True(models[0].SupportsCompletion);
        Assert.Contains("completion", models[0].Capabilities);
        Assert.Contains("tools", models[0].Capabilities);
        Assert.Contains("thinking", models[0].Capabilities);

        Assert.Equal("nomic-embed-text:latest", models[1].Name);
        Assert.False(models[1].SupportsCompletion);
        Assert.Contains("embedding", models[1].Capabilities);
    }

    [Fact]
    public void ParseModelsFromTagsResponse_EmptyModelsArray_ReturnsEmpty()
    {
        var json = """{"models": []}""";
        var models = OllamaService.ParseModelsFromTagsResponse(json);
        Assert.Empty(models);
    }

    [Fact]
    public void ParseModelsFromTagsResponse_MissingModelsKey_ReturnsEmpty()
    {
        var json = """{"status": "ok"}""";
        var models = OllamaService.ParseModelsFromTagsResponse(json);
        Assert.Empty(models);
    }

    [Fact]
    public void ParseModelsFromTagsResponse_MissingDetails_StillParsesModel()
    {
        var json = """
        {
          "models": [
            {
              "name": "minimal-model:latest",
              "size": 1000000
            }
          ]
        }
        """;

        var models = OllamaService.ParseModelsFromTagsResponse(json);
        Assert.Single(models);
        Assert.Equal("minimal-model:latest", models[0].Name);
        Assert.Null(models[0].ParameterSize);
        Assert.Null(models[0].QuantizationLevel);
        Assert.True(models[0].SupportsCompletion);
    }

    [Fact]
    public void ParseModelsFromTagsResponse_InvalidJson_ReturnsEmpty()
    {
        var models = OllamaService.ParseModelsFromTagsResponse("not json at all");
        Assert.Empty(models);
    }

    [Fact]
    public void ParseModelsFromTagsResponse_SetsIsCloud_WhenFlagProvided()
    {
        var json = """{"models": [{"name": "model:latest", "size": 100}]}""";
        var models = OllamaService.ParseModelsFromTagsResponse(json, isCloud: true);
        Assert.Single(models);
        Assert.True(models[0].IsCloud);
    }

    // ── Parse Pull Progress Lines ───────────────────────────────────────

    [Fact]
    public void ParsePullProgressLine_ManifestStatus()
    {
        var line = """{"status":"pulling manifest"}""";
        var progress = OllamaService.ParsePullProgressLine(line);
        Assert.Equal("pulling manifest", progress.Status);
        Assert.False(progress.IsComplete);
        Assert.Null(progress.Percent);
    }

    [Fact]
    public void ParsePullProgressLine_DownloadProgress()
    {
        var line = """{"status":"downloading 5d65f5a82356","digest":"sha256:5d65f5a82356","total":5225388164,"completed":2612694082}""";
        var progress = OllamaService.ParsePullProgressLine(line);
        Assert.Equal("downloading 5d65f5a82356", progress.Status);
        Assert.Equal(5225388164L, progress.TotalBytes);
        Assert.Equal(2612694082L, progress.CompletedBytes);
        Assert.NotNull(progress.Percent);
        Assert.InRange(progress.Percent!.Value, 49.9, 50.1);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void ParsePullProgressLine_SuccessStatus()
    {
        var line = """{"status":"success"}""";
        var progress = OllamaService.ParsePullProgressLine(line);
        Assert.Equal("success", progress.Status);
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public void ParsePullProgressLine_ErrorResponse()
    {
        var line = """{"error":"model 'xyz' not found, try pulling it first"}""";
        var progress = OllamaService.ParsePullProgressLine(line);
        Assert.True(progress.IsComplete);
        Assert.NotNull(progress.ErrorMessage);
        Assert.Contains("not found", progress.ErrorMessage);
    }

    [Fact]
    public void ParsePullProgressLine_InvalidJson_DoesNotThrow()
    {
        var progress = OllamaService.ParsePullProgressLine("not json");
        Assert.Equal("not json", progress.Status);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void ParsePullProgressLine_ZeroTotal_NullPercent()
    {
        var line = """{"status":"downloading","total":0,"completed":0}""";
        var progress = OllamaService.ParsePullProgressLine(line);
        Assert.Null(progress.Percent);
    }

    [Fact]
    public async System.Threading.Tasks.Task PullModelAsync_UsesOnlyAsynchronousStreamReads()
    {
        var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StreamContent(new AsyncOnlyReadStream(
                System.Text.Encoding.UTF8.GetBytes("""{"status":"success"}\n""")))
        };
        var handler = new StaticResponseHttpMessageHandler(response);
        using var httpClient = new System.Net.Http.HttpClient(handler);
        var service = new OllamaService(
            httpClient,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaService>.Instance);
        var progress = new InlineProgress<OllamaPullProgress>();

        await service.PullModelAsync("http://localhost:11434", "qwen3:8b", progress: progress);

        Assert.Contains(progress.Values, update => update.IsComplete && update.ErrorMessage is null);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteModelAsync_SendsDeleteRequestWithModelName()
    {
        var handler = new CapturingHttpMessageHandler();
        using var httpClient = new System.Net.Http.HttpClient(handler);
        var service = new OllamaService(
            httpClient,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaService>.Instance);

        await service.DeleteModelAsync("http://localhost:11434", "qwen3:8b");

        Assert.Equal(System.Net.Http.HttpMethod.Delete, handler.Method);
        Assert.Equal("http://localhost:11434/api/delete", handler.RequestUri?.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("qwen3:8b", body.RootElement.GetProperty("model").GetString());
    }

    // ── Ollama Error Response Parsing Tests ─────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task OllamaProvider_WithStringError_ReturnsFriendlyErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.Unauthorized,
            """{"error": "Unauthorized"}""");
        var httpClient = new System.Net.Http.HttpClient(handler);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Infrastructure.AI.Providers.OllamaProvider>.Instance;
        var provider = new PocketMC.Infrastructure.AI.Providers.OllamaProvider(httpClient, logger);

        var result = await provider.ValidateKeyAsync("invalid-key", "deepseek-v4.1-flash", "https://ollama.com/api/chat");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Authentication failed", result.Error);
        Assert.Contains("Unauthorized", result.Error);
        Assert.DoesNotContain("The requested operation requires an element of type", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task OllamaProvider_WithModelNotFoundError_ReturnsFriendlyErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.NotFound,
            """{"error": "model 'deepseek-v4.1-flash' not found"}""");
        var httpClient = new System.Net.Http.HttpClient(handler);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Infrastructure.AI.Providers.OllamaProvider>.Instance;
        var provider = new PocketMC.Infrastructure.AI.Providers.OllamaProvider(httpClient, logger);

        var result = await provider.ValidateKeyAsync("key", "deepseek-v4.1-flash", "https://ollama.com/api/chat");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Not found", result.Error);
        Assert.Contains("model 'deepseek-v4.1-flash' not found", result.Error);
        Assert.DoesNotContain("The requested operation requires an element of type", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task OllamaProvider_WithObjectError_ReturnsFriendlyErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.Unauthorized,
            """{"error": {"message": "Invalid API key provided"}}""");
        var httpClient = new System.Net.Http.HttpClient(handler);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Infrastructure.AI.Providers.OllamaProvider>.Instance;
        var provider = new PocketMC.Infrastructure.AI.Providers.OllamaProvider(httpClient, logger);

        var result = await provider.ValidateKeyAsync("key", "llama3.2", "https://ollama.com/api/chat");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Invalid API key provided", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task OllamaProvider_WithSuccessfulChatResponse_ReturnsContent()
    {
        var handler = new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK,
            """{"message": {"role": "assistant", "content": "OK"}}""");
        var httpClient = new System.Net.Http.HttpClient(handler);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Infrastructure.AI.Providers.OllamaProvider>.Instance;
        var provider = new PocketMC.Infrastructure.AI.Providers.OllamaProvider(httpClient, logger);

        var result = await provider.ValidateKeyAsync("key", "qwen3:8b", "http://localhost:11434/api/chat");

        Assert.True(result.Success);
        Assert.Equal("OK", result.Content);
    }

    private class FakeHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly System.Net.HttpStatusCode _statusCode;
        private readonly string _content;

        public FakeHttpMessageHandler(System.Net.HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            var response = new System.Net.Http.HttpResponseMessage(_statusCode)
            {
                Content = new System.Net.Http.StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            };
            return System.Threading.Tasks.Task.FromResult(response);
        }
    }

    private sealed class StaticResponseHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly System.Net.Http.HttpResponseMessage _response;

        public StaticResponseHttpMessageHandler(System.Net.Http.HttpResponseMessage response)
        {
            _response = response;
        }

        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(_response);
    }

    private sealed class CapturingHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        public System.Net.Http.HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    private sealed class AsyncOnlyReadStream : System.IO.Stream
    {
        private readonly byte[] _content;
        private int _position;

        public AsyncOnlyReadStream(byte[] content)
        {
            _content = content;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Synchronous reads are not allowed.");

        public override int Read(Span<byte> buffer) =>
            throw new InvalidOperationException("Synchronous reads are not allowed.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, _content.Length - _position);
            _content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override System.Threading.Tasks.Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InlineProgress<T> : System.IProgress<T>
    {
        public List<T> Values { get; } = new();

        public void Report(T value) => Values.Add(value);
    }
}
