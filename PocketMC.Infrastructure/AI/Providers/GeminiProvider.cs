using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.AI.Providers;

public class GeminiProvider : BaseLlmProvider
{
    public GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger) : base(httpClient, logger) { }

    public override AiProviderType ProviderType => AiProviderType.Gemini;

    protected override (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent)
    {
        var m = string.IsNullOrWhiteSpace(model) ? "gemini-2.0-flash" : model;
        var e = string.IsNullOrWhiteSpace(endpoint) ? "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent" : endpoint;
        var url = e.Contains("{0}") ? string.Format(e, m) + $"?key={apiKey}" : $"{e}?key={apiKey}";
        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = $"{systemPrompt}\n\n{userContent}" } } } },
            generationConfig = new { temperature = 0.4, maxOutputTokens = 4096 }
        };
        return (url, JsonSerializer.Serialize(body), "Bearer NOT_USED");
    }

    protected override string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;
    }
}
