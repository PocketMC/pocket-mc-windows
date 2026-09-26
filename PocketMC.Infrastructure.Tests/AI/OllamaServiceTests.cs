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
}
