using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.AI.Providers;

public abstract class BaseLlmProvider : ILlmProvider
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;

    protected BaseLlmProvider(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public abstract AiProviderType ProviderType { get; }

    protected abstract (string url, string body, string auth) BuildRequest(string apiKey, string model, string endpoint, string systemPrompt, string userContent);
    protected abstract string ExtractContent(string json);

    public async Task<AiApiResult> GenerateCompletionAsync(string apiKey, string model, string endpoint, string systemPrompt, string userContent, CancellationToken ct = default)
    {
        try
        {
            var (url, body, auth) = BuildRequest(apiKey, model, endpoint, systemPrompt, userContent);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            if (ProviderType == AiProviderType.Claude)
            {
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (ProviderType != AiProviderType.Gemini && !string.IsNullOrWhiteSpace(auth))
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

            var content = ExtractContent(responseBody);
            return AiApiResult.Ok(content);
        }
        catch (TaskCanceledException)
        {
            return AiApiResult.Fail("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI API request failed for provider {Provider}.", ProviderType);
            return AiApiResult.Fail($"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during AI API call for provider {Provider}.", ProviderType);
            return AiApiResult.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<AiApiResult> ValidateKeyAsync(string apiKey, string model, string endpoint, CancellationToken ct = default)
    {
        return await GenerateCompletionAsync(apiKey, model, endpoint, "Reply with exactly the word OK and nothing else.", "Connectivity test.", ct);
    }

    private string ParseApiErrorMessage(string responseBody, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var errorObj))
                {
                    if (errorObj.ValueKind == JsonValueKind.String)
                    {
                        var str = errorObj.GetString();
                        if (!string.IsNullOrWhiteSpace(str))
                            return FormatErrorMessage(str, statusCode);
                    }
                    else if (errorObj.ValueKind == JsonValueKind.Object)
                    {
                        if (errorObj.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                        {
                            var str = msg.GetString();
                            if (!string.IsNullOrWhiteSpace(str))
                                return FormatErrorMessage(str, statusCode);
                        }
                    }
                }

                if (root.TryGetProperty("message", out var topMsg) && topMsg.ValueKind == JsonValueKind.String)
                {
                    var str = topMsg.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                        return FormatErrorMessage(str, statusCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse AI API error response body as JSON.");
        }

        return FormatDefaultHttpError(statusCode, responseBody);
    }

    private static string FormatErrorMessage(string rawMessage, int statusCode)
    {
        var msg = rawMessage.Trim();

        if (statusCode == 401 || string.Equals(msg, "Unauthorized", StringComparison.OrdinalIgnoreCase))
            return $"Authentication failed (HTTP 401): {msg}. Verify your API key.";

        if (statusCode == 404)
            return $"Not found (HTTP 404): {msg}.";

        if (statusCode == 429)
            return $"Rate limited (HTTP 429): {msg}. Please wait before retrying.";

        return $"API error (HTTP {statusCode}): {msg}";
    }

    private static string FormatDefaultHttpError(int statusCode, string responseBody)
    {
        if (statusCode == 401)
            return "Authentication failed (HTTP 401): Invalid or unauthorized API key.";

        if (statusCode == 404)
            return "API endpoint or model not found (HTTP 404).";

        if (statusCode == 429)
            return "Rate limited (HTTP 429): Too many requests. Please wait before retrying.";

        var snippet = responseBody.Length > 150 ? responseBody[..150] + "..." : responseBody;
        return string.IsNullOrWhiteSpace(snippet)
            ? $"API returned HTTP {statusCode}."
            : $"API returned HTTP {statusCode}: {snippet}";
    }
}
