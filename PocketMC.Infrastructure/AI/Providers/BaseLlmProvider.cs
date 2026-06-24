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

    private static string ParseApiErrorMessage(string responseBody, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? $"API error ({statusCode})";
            }

            if (root.TryGetProperty("message", out var topMsg))
                return topMsg.GetString() ?? $"API error ({statusCode})";
        }
        catch { }

        return $"API returned HTTP {statusCode}. {(responseBody.Length > 150 ? responseBody.Substring(0, 150) + "..." : responseBody)}";
    }
}
