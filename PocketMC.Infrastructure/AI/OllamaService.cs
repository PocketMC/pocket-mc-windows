using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.AI;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<OllamaDaemonStatus> CheckDaemonHealthAsync(
        string endpoint, CancellationToken ct = default)
    {
        var baseUrl = NormalizeBaseUrl(endpoint);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/version");
            AttachAuthIfCloud(request, baseUrl, apiKey: null);

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return OllamaDaemonStatus.Offline(
                    $"Ollama returned HTTP {(int)response.StatusCode}: {TruncateBody(body)}");
            }

            string? version = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("version", out var vProp))
                    version = vProp.GetString();
            }
            catch (JsonException)
            {
                version = "unknown";
            }

            return OllamaDaemonStatus.Online(version ?? "unknown");
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            _logger.LogDebug(ex, "Ollama daemon not reachable at {Endpoint}.", baseUrl);
            return OllamaDaemonStatus.Offline(
                "Ollama is not running. Start it from the system tray or run 'ollama serve' in a terminal.");
        }
        catch (TaskCanceledException)
        {
            return OllamaDaemonStatus.Offline("Health check timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking Ollama health at {Endpoint}.", baseUrl);
            return OllamaDaemonStatus.Offline($"Connection error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<OllamaModelInfo>> GetInstalledModelsAsync(
        string endpoint, string? apiKey = null, CancellationToken ct = default)
    {
        var baseUrl = NormalizeBaseUrl(endpoint);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/tags");
            AttachAuthIfNeeded(request, baseUrl, apiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = ParseHttpError(response, body);
                _logger.LogWarning("Ollama /api/tags returned {StatusCode}: {Error}",
                    (int)response.StatusCode, errorMsg);
                return Array.Empty<OllamaModelInfo>();
            }

            return ParseModelsFromTagsResponse(body, isCloud: IsCloudEndpoint(baseUrl));
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            _logger.LogDebug(ex, "Cannot list models: Ollama is not running at {Endpoint}.", baseUrl);
            return Array.Empty<OllamaModelInfo>();
        }
        catch (TaskCanceledException)
        {
            return Array.Empty<OllamaModelInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error listing Ollama models from {Endpoint}.", baseUrl);
            return Array.Empty<OllamaModelInfo>();
        }
    }

    public async Task PullModelAsync(
        string endpoint,
        string modelName,
        string? apiKey = null,
        IProgress<OllamaPullProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be empty.", nameof(modelName));

        var baseUrl = NormalizeBaseUrl(endpoint);
        var pullUrl = $"{baseUrl}/api/pull";

        var requestBody = JsonSerializer.Serialize(new { model = modelName, stream = true });

        using var request = new HttpRequestMessage(HttpMethod.Post, pullUrl)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        AttachAuthIfNeeded(request, baseUrl, apiKey);

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var errorMsg = ParseHttpError(response, errorBody);
                progress?.Report(OllamaPullProgress.Error(errorMsg));
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var progressUpdate = ParsePullProgressLine(line);
                progress?.Report(progressUpdate);

                if (progressUpdate.IsComplete)
                    return;
            }

            progress?.Report(OllamaPullProgress.Success());
        }
        catch (OperationCanceledException)
        {
            progress?.Report(OllamaPullProgress.Error("Download cancelled."));
            throw;
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            progress?.Report(OllamaPullProgress.Error(
                "Ollama is not running. Start it from the system tray or run 'ollama serve'."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling model {Model} from {Endpoint}.", modelName, baseUrl);
            progress?.Report(OllamaPullProgress.Error($"Pull failed: {ex.Message}"));
        }
        finally
        {
            response?.Dispose();
        }
    }

    // ── URL Normalization ──────────────────────────────────────────────

    internal static string NormalizeBaseUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "http://localhost:11434";

        var trimmed = endpoint.Trim().TrimEnd('/');

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = trimmed.Contains("ollama.com", StringComparison.OrdinalIgnoreCase)
                ? "https://" + trimmed
                : "http://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return "http://localhost:11434";

        var basePath = uri.AbsolutePath.TrimEnd('/');

        if (basePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            basePath.Equals("/api", StringComparison.OrdinalIgnoreCase))
        {
            basePath = string.Empty;
        }

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}{basePath}";
    }

    internal static bool IsCloudEndpoint(string baseUrl)
    {
        return baseUrl.Contains("ollama.com", StringComparison.OrdinalIgnoreCase);
    }

    // ── JSON Parsing ───────────────────────────────────────────────────

    internal static IReadOnlyList<OllamaModelInfo> ParseModelsFromTagsResponse(
        string json, bool isCloud = false)
    {
        var models = new List<OllamaModelInfo>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var modelsArray))
                return models;

            foreach (var model in modelsArray.EnumerateArray())
            {
                var name = model.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var tag = model.TryGetProperty("model", out var t) ? t.GetString() ?? name : name;
                var size = model.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                string? parameterSize = null;
                string? quantLevel = null;
                string? family = null;
                var capabilities = new List<string>();

                if (model.TryGetProperty("details", out var details))
                {
                    parameterSize = details.TryGetProperty("parameter_size", out var ps) ? ps.GetString() : null;
                    quantLevel = details.TryGetProperty("quantization_level", out var ql) ? ql.GetString() : null;
                    family = details.TryGetProperty("family", out var f) ? f.GetString() : null;
                }

                if (model.TryGetProperty("capabilities", out var caps) &&
                    caps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cap in caps.EnumerateArray())
                    {
                        var capStr = cap.GetString();
                        if (!string.IsNullOrEmpty(capStr))
                            capabilities.Add(capStr);
                    }
                }

                models.Add(new OllamaModelInfo
                {
                    Name = name,
                    ModelTag = tag,
                    SizeBytes = size,
                    ParameterSize = parameterSize,
                    QuantizationLevel = quantLevel,
                    Family = family,
                    IsCloud = isCloud,
                    Capabilities = capabilities
                });
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse Ollama tags response: {ex.Message}");
        }

        return models;
    }

    internal static OllamaPullProgress ParsePullProgressLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "" : "";
            var digest = root.TryGetProperty("digest", out var dProp) ? dProp.GetString() : null;
            long? total = root.TryGetProperty("total", out var tProp) && tProp.ValueKind == JsonValueKind.Number
                ? tProp.GetInt64() : null;
            long? completed = root.TryGetProperty("completed", out var cProp) && cProp.ValueKind == JsonValueKind.Number
                ? cProp.GetInt64() : null;

            bool isComplete = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);

            string? errorMessage = null;
            if (root.TryGetProperty("error", out var errProp))
            {
                errorMessage = errProp.GetString();
                isComplete = true;
            }

            return new OllamaPullProgress
            {
                Status = status,
                Digest = digest,
                TotalBytes = total,
                CompletedBytes = completed,
                IsComplete = isComplete,
                ErrorMessage = errorMessage
            };
        }
        catch (JsonException)
        {
            return new OllamaPullProgress
            {
                Status = line,
                IsComplete = false
            };
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void AttachAuthIfNeeded(HttpRequestMessage request, string baseUrl, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
        else if (IsCloudEndpoint(baseUrl))
        {
            // Cloud requests without a key will likely fail with 401,
            // but we still let them through so the server error is surfaced.
        }
    }

    private static void AttachAuthIfCloud(HttpRequestMessage request, string baseUrl, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static bool IsConnectionRefused(HttpRequestException ex)
    {
        if (ex.InnerException is SocketException socketEx)
        {
            return socketEx.SocketErrorCode == SocketError.ConnectionRefused ||
                   socketEx.SocketErrorCode == SocketError.HostUnreachable ||
                   socketEx.SocketErrorCode == SocketError.HostNotFound;
        }
        return false;
    }

    private static string ParseHttpError(HttpResponseMessage response, string body)
    {
        var statusCode = (int)response.StatusCode;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errProp))
            {
                var msg = errProp.GetString();
                if (!string.IsNullOrWhiteSpace(msg))
                    return FormatHttpError(statusCode, msg);
            }
        }
        catch (JsonException) { }

        return statusCode switch
        {
            401 => "Invalid API key. Verify your key at ollama.com/settings/keys.",
            404 => "Model not found. Try pulling the model first.",
            429 => "Request limit reached. Please wait a moment before retrying.",
            >= 500 and < 600 => $"Ollama server error (HTTP {statusCode}). The service may be temporarily unavailable.",
            _ => $"Ollama returned HTTP {statusCode}: {TruncateBody(body)}"
        };
    }

    private static string FormatHttpError(int statusCode, string message)
    {
        if (statusCode == 401)
            return $"Authentication failed: {message}. Verify your key at ollama.com/settings/keys.";

        if (statusCode == 404 &&
            message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return $"{message}. Use the Download tab to pull the model first.";
        }

        return $"HTTP {statusCode}: {message}";
    }

    private static string TruncateBody(string body)
    {
        return body.Length > 200 ? body[..200] + "..." : body;
    }
}
