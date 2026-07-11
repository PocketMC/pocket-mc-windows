using PocketMC.Application.Services.Shell;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.Telemetry;


namespace PocketMC.Infrastructure.Tunnel;

public sealed class PlayitPartnerAgentVersion
{
    [JsonPropertyName("versionMajor")]
    public int VersionMajor { get; set; }

    [JsonPropertyName("versionMinor")]
    public int VersionMinor { get; set; }

    [JsonPropertyName("versionPatch")]
    public int VersionPatch { get; set; }

    public override string ToString() => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
}

public sealed class PlayitPartnerCreateAgentRequest
{
    [JsonPropertyName("setupCode")]
    public string SetupCode { get; set; } = string.Empty;

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = "PocketMC";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "windows";

    [JsonPropertyName("agentVersion")]
    public PlayitPartnerAgentVersion AgentVersion { get; set; } = new();
}

public sealed class PlayitPartnerCreateAgentResponse
{
    [JsonPropertyName("accountId")]
    public long? AccountId { get; set; }

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("agentSecretKey")]
    public string AgentSecretKey { get; set; } = string.Empty;

    [JsonPropertyName("agentOverLimit")]
    public bool AgentOverLimit { get; set; }

    [JsonPropertyName("connectedEmail")]
    public string? ConnectedEmail { get; set; }
}

public sealed class PlayitPartnerCreateAgentResult
{
    public bool Success { get; set; }
    public PlayitPartnerCreateAgentResponse? Response { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class PlayitPartnerProvisioningClient
{
    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PlayitPartnerProvisioningClient> _logger;

    public PlayitPartnerProvisioningClient(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        ILogger<PlayitPartnerProvisioningClient> logger,
        HttpClient? httpClient = null)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public bool IsConfigured => _settingsManager.GetPlayitPartnerBackendUrls(_applicationState.Settings).Count > 0;

    public Uri? GetSetupPageUri()
    {
        return Uri.TryCreate("https://playit.gg/l/setup-third-party", UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    public async Task<PlayitPartnerCreateAgentResult> CreateAgentAsync(
        PlayitPartnerCreateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SetupCode))
        {
            return new PlayitPartnerCreateAgentResult
            {
                Success = false,
                ErrorMessage = "Enter a Playit setup code first."
            };
        }

        if (_settingsManager.GetPlayitPartnerBackendUrls(_applicationState.Settings).Count == 0)
        {
            return new PlayitPartnerCreateAgentResult
            {
                Success = false,
                ErrorMessage = "PocketMC Playit provisioning backend is not configured."
            };
        }

        try
        {
            using HttpResponseMessage response = await PostWithFallbackAsync("api/playit/partner/create-agent", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                return new PlayitPartnerCreateAgentResult
                {
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(body)
                        ? $"Provisioning backend returned {(int)response.StatusCode}."
                        : body
                };
            }

            PlayitPartnerCreateAgentResponse? payload =
                await response.Content.ReadFromJsonAsync<PlayitPartnerCreateAgentResponse>(cancellationToken: cancellationToken);
            if (payload == null || string.IsNullOrWhiteSpace(payload.AgentId) || string.IsNullOrWhiteSpace(payload.AgentSecretKey))
            {
                return new PlayitPartnerCreateAgentResult
                {
                    Success = false,
                    ErrorMessage = "Provisioning backend returned an incomplete Playit agent response."
                };
            }

            return new PlayitPartnerCreateAgentResult
            {
                Success = true,
                Response = payload
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to provision a Playit partner agent.");
            return new PlayitPartnerCreateAgentResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<HttpResponseMessage> PostWithFallbackAsync(string path, object payload, CancellationToken ct)
    {
        var urls = _settingsManager.GetPlayitPartnerBackendUrls(_applicationState.Settings);
        Exception? lastException = null;

        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            
            try
            {
                var endpoint = new Uri(new Uri(url.TrimEnd('/') + "/"), path);
                var response = await _httpClient.PostAsJsonAsync(endpoint, payload, ct);
                if (response.IsSuccessStatusCode)
                    return response;
                
                // Dispose non-success to try next, or wait... maybe we want to return non-success if ALL fail?
                // For simplicity, let's just return the last non-success response if all fail.
                if (url == urls[urls.Count - 1]) return response;
                response.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Proxy request threw exception to {Url}. Trying next.", url);
                lastException = ex;
            }
        }

        if (lastException != null) throw lastException;
        throw new HttpRequestException("All Playit partner backends failed.");
    }
}
