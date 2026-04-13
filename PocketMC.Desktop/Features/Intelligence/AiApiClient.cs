using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Intelligence;

/// <summary>
/// Supported AI providers for session summarization.
/// </summary>
public enum AiProviderType
{
    Gemini,
    OpenAI,
    Claude,
    Mistral,
    Groq
}

/// <summary>
/// Provider-agnostic AI API client that routes requests to the correct endpoint
/// based on the user's selected provider.
/// </summary>
public class AiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiApiClient> _logger;

    private static readonly Dictionary<AiProviderType, ProviderConfig> Providers = new()
    {
        [AiProviderType.Gemini] = new ProviderConfig
        {
            DisplayName = "Google Gemini",
            BuildRequest = (apiKey, systemPrompt, userContent) =>
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
                var body = new
                {
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = $"{systemPrompt}\n\n{userContent}" } } }
                    },
                    generationConfig = new { temperature = 0.4, maxOutputTokens = 4096 }
                };
                return (url, JsonSerializer.Serialize(body), "Bearer NOT_USED");
            },
            ExtractContent = (json) =>
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;
            }
        },
        [AiProviderType.OpenAI] = new ProviderConfig
        {
            DisplayName = "OpenAI",
            BuildRequest = (apiKey, systemPrompt, userContent) =>
            {
                var url = "https://api.openai.com/v1/chat/completions";
                var body = new
                {
                    model = "gpt-4o-mini",
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    temperature = 0.4,
                    max_tokens = 4096
                };
                return (url, JsonSerializer.Serialize(body), $"Bearer {apiKey}");
            },
            ExtractContent = (json) =>
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
        },
        [AiProviderType.Claude] = new ProviderConfig
        {
            DisplayName = "Anthropic Claude",
            BuildRequest = (apiKey, systemPrompt, userContent) =>
            {
                var url = "https://api.anthropic.com/v1/messages";
                var body = new
                {
                    model = "claude-3-5-sonnet-20241022",
                    max_tokens = 4096,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userContent }
                    }
                };
                return (url, JsonSerializer.Serialize(body), $"ANTHROPIC_KEY {apiKey}");
            },
            ExtractContent = (json) =>
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;
            }
        },
        [AiProviderType.Mistral] = new ProviderConfig
        {
            DisplayName = "Mistral AI",
            BuildRequest = (apiKey, systemPrompt, userContent) =>
            {
                var url = "https://api.mistral.ai/v1/chat/completions";
                var body = new
                {
                    model = "mistral-small-latest",
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    temperature = 0.4,
                    max_tokens = 4096
                };
                return (url, JsonSerializer.Serialize(body), $"Bearer {apiKey}");
            },
            ExtractContent = (json) =>
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
        },
        [AiProviderType.Groq] = new ProviderConfig
        {
            DisplayName = "Groq",
            BuildRequest = (apiKey, systemPrompt, userContent) =>
            {
                var url = "https://api.groq.com/openai/v1/chat/completions";
                var body = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    temperature = 0.4,
                    max_tokens = 4096
                };
                return (url, JsonSerializer.Serialize(body), $"Bearer {apiKey}");
            },
            ExtractContent = (json) =>
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
        }
    };

    public AiApiClient(HttpClient httpClient, ILogger<AiApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public static IReadOnlyList<string> GetProviderNames()
    {
        var names = new List<string>();
        foreach (AiProviderType p in Enum.GetValues(typeof(AiProviderType)))
            names.Add(Providers[p].DisplayName);
        return names;
    }

    public static string GetDisplayName(AiProviderType provider) =>
        Providers.TryGetValue(provider, out var cfg) ? cfg.DisplayName : provider.ToString();

    public static AiProviderType ParseProvider(string name)
    {
        foreach (var kvp in Providers)
        {
            if (kvp.Value.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }
        return AiProviderType.Gemini;
    }

    /// <summary>
    /// Send a summarization request to the configured AI provider.
    /// </summary>
    public async Task<AiApiResult> SendAsync(AiProviderType provider, string apiKey, string systemPrompt, string userContent, CancellationToken ct = default)
    {
        if (!Providers.TryGetValue(provider, out var config))
            return AiApiResult.Fail($"Unknown provider: {provider}");

        try
        {
            var (url, body, auth) = config.BuildRequest(apiKey, systemPrompt, userContent);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            // Set auth header based on provider
            if (provider == AiProviderType.Claude)
            {
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (provider != AiProviderType.Gemini)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                var friendlyError = ParseApiErrorMessage(responseBody, (int)response.StatusCode);
                return AiApiResult.Fail(friendlyError);
            }

            var content = config.ExtractContent(responseBody);
            return AiApiResult.Ok(content);
        }
        catch (TaskCanceledException)
        {
            return AiApiResult.Fail("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI API request failed for provider {Provider}.", provider);
            return AiApiResult.Fail($"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during AI API call for provider {Provider}.", provider);
            return AiApiResult.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates the API key by sending a minimal test request.
    /// Returns the full result so the UI can show the specific error.
    /// </summary>
    public async Task<AiApiResult> ValidateKeyAsync(AiProviderType provider, string apiKey, CancellationToken ct = default)
    {
        return await SendAsync(provider, apiKey, "Reply with exactly the word OK and nothing else.", "Connectivity test.", ct);
    }

    /// <summary>
    /// Extracts a human-readable error message from the API error response body.
    /// </summary>
    private static string ParseApiErrorMessage(string responseBody, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Gemini format: { "error": { "message": "..." } }
            if (root.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? $"API error ({statusCode})";
            }

            // OpenAI / Groq / Mistral format: { "error": { "message": "..." } }
            // Already handled above

            // Claude format: { "error": { "message": "..." } }
            // Already handled above

            // Fallback: try top-level "message"
            if (root.TryGetProperty("message", out var topMsg))
                return topMsg.GetString() ?? $"API error ({statusCode})";
        }
        catch
        {
            // JSON parsing failed — return raw truncated
        }

        return $"API returned HTTP {statusCode}. {(responseBody.Length > 150 ? responseBody[..150] + "..." : responseBody)}";
    }

    private static string TruncateError(string body) =>
        body.Length > 200 ? body[..200] + "..." : body;

    private class ProviderConfig
    {
        public string DisplayName { get; init; } = string.Empty;
        public Func<string, string, string, (string url, string body, string auth)> BuildRequest { get; init; } = null!;
        public Func<string, string> ExtractContent { get; init; } = null!;
    }
}

public class AiApiResult
{
    public bool Success { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Error { get; init; }

    public static AiApiResult Ok(string content) => new() { Success = true, Content = content };
    public static AiApiResult Fail(string error) => new() { Success = false, Error = error };
}
