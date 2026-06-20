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
    Groq,
    Ollama
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
            DefaultModel = "gemini-2.0-flash",
            DefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent",
            BuildRequest = (apiKey, model, endpoint, systemPrompt, userContent) =>
            {
                var m = string.IsNullOrWhiteSpace(model) ? "gemini-2.0-flash" : model;
                var e = string.IsNullOrWhiteSpace(endpoint) ? "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent" : endpoint;
                var url = e.Contains("{0}") ? string.Format(e, m) + $"?key={apiKey}" : $"{e}?key={apiKey}";
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
            DefaultModel = "gpt-4o-mini",
            DefaultEndpoint = "https://api.openai.com/v1/chat/completions",
            BuildRequest = (apiKey, model, endpoint, systemPrompt, userContent) =>
            {
                var m = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
                var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1/chat/completions" : endpoint;
                var body = new
                {
                    model = m,
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
            DefaultModel = "claude-3-5-haiku-latest",
            DefaultEndpoint = "https://api.anthropic.com/v1/messages",
            BuildRequest = (apiKey, model, endpoint, systemPrompt, userContent) =>
            {
                var m = string.IsNullOrWhiteSpace(model) ? "claude-3-5-haiku-latest" : model;
                var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.anthropic.com/v1/messages" : endpoint;
                var body = new
                {
                    model = m,
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
            DefaultModel = "mistral-large-latest",
            DefaultEndpoint = "https://api.mistral.ai/v1/chat/completions",
            BuildRequest = (apiKey, model, endpoint, systemPrompt, userContent) =>
            {
                var m = string.IsNullOrWhiteSpace(model) ? "mistral-large-latest" : model;
                var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.mistral.ai/v1/chat/completions" : endpoint;
                var body = new
                {
                    model = m,
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
            DefaultModel = "llama-3.3-70b-versatile",
            DefaultEndpoint = "https://api.groq.com/openai/v1/chat/completions",
            BuildRequest = (apiKey, model, endpoint, systemPrompt, userContent) =>
            {
                var m = string.IsNullOrWhiteSpace(model) ? "llama-3.3-70b-versatile" : model;
                var url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.groq.com/openai/v1/chat/completions" : endpoint;
                var body = new
                {
                    model = m,
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
        [AiProviderType.Ollama] = new ProviderConfig
        {
            DisplayName = "Ollama",
            DefaultModel = "llama3.2",
            DefaultEndpoint = "http://localhost:11434/api/chat",
            BuildRequest = (apiKey, model, endpoint, systemPrompt, userContent) =>
            {
                var m = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;
                var url = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434/api/chat" : endpoint;
                var body = new
                {
                    model = m,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    stream = false,
                    options = new { temperature = 0.4, num_predict = 4096 }
                };
                var auth = string.IsNullOrWhiteSpace(apiKey) ? "" : $"Bearer {apiKey}";
                return (url, JsonSerializer.Serialize(body), auth);
            },
            ExtractContent = (json) =>
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // Handle Ollama native response
                if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                    return content.GetString() ?? string.Empty;
                    
                // Fallback for OpenAI compatible response
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    return choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
                    
                return string.Empty;
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

    public static (string DefaultModel, string DefaultEndpoint) GetProviderDefaults(AiProviderType provider)
    {
        return Providers.TryGetValue(provider, out var cfg) 
            ? (cfg.DefaultModel, cfg.DefaultEndpoint) 
            : (string.Empty, string.Empty);
    }

    /// <summary>
    /// Send a summarization request to the configured AI provider.
    /// </summary>
    public async Task<AiApiResult> SendAsync(AiProviderType provider, string apiKey, string model, string endpoint, string systemPrompt, string userContent, CancellationToken ct = default)
    {
        if (!Providers.TryGetValue(provider, out var config))
            return AiApiResult.Fail($"Unknown provider: {provider}");

        try
        {
            var (url, body, auth) = config.BuildRequest(apiKey, model, endpoint, systemPrompt, userContent);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            // Set auth header based on provider
            if (provider == AiProviderType.Claude)
            {
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (provider != AiProviderType.Gemini && !string.IsNullOrWhiteSpace(auth))
            {
                request.Headers.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(auth);
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
    public async Task<AiApiResult> ValidateKeyAsync(AiProviderType provider, string apiKey, string model, string endpoint, CancellationToken ct = default)
    {
        return await SendAsync(provider, apiKey, model, endpoint, "Reply with exactly the word OK and nothing else.", "Connectivity test.", ct);
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
        public string DefaultModel { get; init; } = string.Empty;
        public string DefaultEndpoint { get; init; } = string.Empty;
        public Func<string, string, string, string, string, (string url, string body, string auth)> BuildRequest { get; init; } = null!;
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
