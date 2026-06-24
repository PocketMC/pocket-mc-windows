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
        var m = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;
        var url = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434/api/chat" : endpoint;
        var body = new { model = m, messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userContent } }, stream = false, options = new { temperature = 0.4, num_predict = 4096 } };
        return (url, JsonSerializer.Serialize(body), "");
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
            return content.GetString() ?? string.Empty;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return string.Empty;
    }
}
