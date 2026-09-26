using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.AI.Providers;

public class OpenAiProvider : BaseLlmProvider
{
    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger) : base(httpClient, logger) { }
    public override AiProviderType ProviderType => AiProviderType.OpenAI;

    protected override (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent)
    {
        var m = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
        var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1/chat/completions" : endpoint;
        var body = new { model = m, messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userContent } }, temperature = 0.4, max_tokens = 4096 };
        return (url, JsonSerializer.Serialize(body), $"Bearer {apiKey}");
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}

public class ClaudeProvider : BaseLlmProvider
{
    public ClaudeProvider(HttpClient httpClient, ILogger<ClaudeProvider> logger) : base(httpClient, logger) { }
    public override AiProviderType ProviderType => AiProviderType.Claude;

    protected override (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent)
    {
        var m = string.IsNullOrWhiteSpace(model) ? "claude-3-5-haiku-latest" : model;
        var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.anthropic.com/v1/messages" : endpoint;
        var body = new { model = m, max_tokens = 4096, system = systemPrompt, messages = new[] { new { role = "user", content = userContent } } };
        return (url, JsonSerializer.Serialize(body), $"ANTHROPIC_KEY {apiKey}");
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }
}

public class MistralProvider : BaseLlmProvider
{
    public MistralProvider(HttpClient httpClient, ILogger<MistralProvider> logger) : base(httpClient, logger) { }
    public override AiProviderType ProviderType => AiProviderType.Mistral;

    protected override (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent)
    {
        var m = string.IsNullOrWhiteSpace(model) ? "mistral-large-latest" : model;
        var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.mistral.ai/v1/chat/completions" : endpoint;
        var body = new { model = m, messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userContent } }, temperature = 0.4, max_tokens = 4096 };
        return (url, JsonSerializer.Serialize(body), $"Bearer {apiKey}");
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}

public class GroqProvider : BaseLlmProvider
{
    public GroqProvider(HttpClient httpClient, ILogger<GroqProvider> logger) : base(httpClient, logger) { }
    public override AiProviderType ProviderType => AiProviderType.Groq;

    protected override (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent)
    {
        var m = string.IsNullOrWhiteSpace(model) ? "llama-3.3-70b-versatile" : model;
        var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.groq.com/openai/v1/chat/completions" : endpoint;
        var body = new { model = m, messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userContent } }, temperature = 0.4, max_tokens = 4096 };
        return (url, JsonSerializer.Serialize(body), $"Bearer {apiKey}");
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}

public class OllamaProvider : BaseLlmProvider
{
    public OllamaProvider(HttpClient httpClient, ILogger<OllamaProvider> logger) : base(httpClient, logger) { }
    public override AiProviderType ProviderType => AiProviderType.Ollama;

    protected override (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent)
    {
        var m = string.IsNullOrWhiteSpace(model) ? "qwen3:8b" : model;
        var url = NormalizeChatUrl(endpoint);
        var body = new { model = m, messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userContent } }, stream = false, options = new { temperature = 0.4, num_predict = 4096 } };

        var auth = string.IsNullOrWhiteSpace(apiKey) ? "" : $"Bearer {apiKey.Trim()}";
        return (url, JsonSerializer.Serialize(body), auth);
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("message", out var choiceMsg) &&
                    choiceMsg.ValueKind == JsonValueKind.Object &&
                    choiceMsg.TryGetProperty("content", out var choiceContent) &&
                    choiceContent.ValueKind == JsonValueKind.String)
                {
                    return choiceContent.GetString() ?? string.Empty;
                }
            }

            if (root.TryGetProperty("error", out var errorProp))
            {
                string? errorMsg = null;
                if (errorProp.ValueKind == JsonValueKind.String)
                    errorMsg = errorProp.GetString();
                else if (errorProp.ValueKind == JsonValueKind.Object &&
                         errorProp.TryGetProperty("message", out var errMessageProp) &&
                         errMessageProp.ValueKind == JsonValueKind.String)
                    errorMsg = errMessageProp.GetString();

                errorMsg ??= "Unknown Ollama error";

                if (errorMsg.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"{errorMsg}. Pull the model first using 'ollama pull {GetModelFromError(errorMsg)}' or select an available model.");

                throw new InvalidOperationException(errorMsg);
            }
        }

        return string.Empty;
    }

    private static string NormalizeChatUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "http://localhost:11434/api/chat";

        var trimmed = endpoint.Trim().TrimEnd('/');
        if (!trimmed.Contains("://", System.StringComparison.Ordinal))
        {
            trimmed = trimmed.Contains("ollama.com", System.StringComparison.OrdinalIgnoreCase)
                ? "https://" + trimmed
                : "http://" + trimmed;
        }

        if (trimmed.EndsWith("/api/chat", System.StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.EndsWith("/api", System.StringComparison.OrdinalIgnoreCase))
            return trimmed + "/chat";

        return trimmed + "/api/chat";
    }

    private static string GetModelFromError(string errorMsg)
    {
        var marker = "model '";
        var startIdx = errorMsg.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return "<model-name>";
        startIdx += marker.Length;
        var endIdx = errorMsg.IndexOf('\'', startIdx);
        return endIdx > startIdx ? errorMsg[startIdx..endIdx] : "<model-name>";
    }
}
