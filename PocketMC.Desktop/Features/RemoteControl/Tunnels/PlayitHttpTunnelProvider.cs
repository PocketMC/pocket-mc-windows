using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Features.RemoteControl.Tunnels;

public sealed class PlayitHttpTunnelProvider : IRemoteTunnelProvider
{
    private const string TunnelName = "pocketmc-remote-dashboard";

    private readonly PlayitApiClient _apiClient;
    private readonly PlayitAgentService _agentService;
    private readonly ILogger<PlayitHttpTunnelProvider> _logger;
    private readonly object _lock = new();
    private string? _publicUrl;
    private string? _errorMessage;
    private DateTimeOffset? _startedAtUtc;

    public PlayitHttpTunnelProvider(
        PlayitApiClient apiClient,
        PlayitAgentService agentService,
        ILogger<PlayitHttpTunnelProvider> logger)
    {
        _apiClient = apiClient;
        _agentService = agentService;
        _logger = logger;
    }

    public string Id => "playit-http";
    public string DisplayName => "PlayIt HTTPS Tunnel";

    public async Task<RemoteTunnelStartResult> StartAsync(
        RemoteTunnelStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_apiClient.HasPartnerConnection())
        {
            return SetError("Connect PlayIt before starting a PlayIt HTTPS remote link.");
        }

        if (!_agentService.IsRunning)
        {
            _agentService.Start();
        }

        TunnelData? existing;
        try
        {
            existing = await FindExistingTunnelAsync(request.LocalPort);
        }
        catch (Exception ex)
        {
            return SetError(ex.Message);
        }

        if (existing != null)
        {
            return SetSuccess(existing.PublicAddress);
        }

        TunnelCreateResult createResult = await _apiClient.CreateHttpTunnelAsync(TunnelName, request.LocalPort);
        if (!createResult.Success)
        {
            string message = createResult.RequiresPlayitPremium
                ? "PlayIt HTTPS remote links require PlayIt Premium. Upgrade or use Cloudflare Quick Tunnel."
                : createResult.ErrorMessage ?? "PlayIt could not create an HTTPS remote tunnel.";
            return SetError(message);
        }

        try
        {
            existing = await FindExistingTunnelAsync(request.LocalPort);
        }
        catch (Exception ex)
        {
            return SetError(ex.Message);
        }

        if (existing == null)
        {
            return SetError("PlayIt HTTPS tunnel was created, but no public URL is allocated yet. Try again in a moment.");
        }

        return SetSuccess(existing.PublicAddress);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _publicUrl = null;
            _errorMessage = null;
            _startedAtUtc = null;
        }

        return Task.CompletedTask;
    }

    public RemoteTunnelStatus GetStatus()
    {
        lock (_lock)
        {
            return new RemoteTunnelStatus
            {
                IsRunning = !string.IsNullOrWhiteSpace(_publicUrl),
                PublicUrl = _publicUrl,
                ErrorMessage = _errorMessage,
                StartedAtUtc = _startedAtUtc
            };
        }
    }

    private async Task<TunnelData?> FindExistingTunnelAsync(int localPort)
    {
        TunnelListResult listResult = await _apiClient.GetTunnelsAsync();
        if (!listResult.Success)
        {
            _logger.LogWarning("PlayIt tunnel list failed for remote dashboard: {Error}", listResult.ErrorMessage);
            throw new InvalidOperationException(listResult.ErrorMessage ?? "PlayIt tunnel list failed.");
        }

        return PlayitApiClient.FindHttpTunnelForPort(listResult.Tunnels, localPort);
    }

    private RemoteTunnelStartResult SetSuccess(string publicAddress)
    {
        string publicUrl = NormalizePublicUrl(publicAddress);
        lock (_lock)
        {
            _publicUrl = publicUrl;
            _errorMessage = null;
            _startedAtUtc ??= DateTimeOffset.UtcNow;
        }

        return new RemoteTunnelStartResult
        {
            Success = true,
            PublicUrl = publicUrl
        };
    }

    private RemoteTunnelStartResult SetError(string message)
    {
        lock (_lock)
        {
            _publicUrl = null;
            _errorMessage = message;
            _startedAtUtc = null;
        }

        return RemoteTunnelStartResult.Failed(message);
    }

    private static string NormalizePublicUrl(string publicAddress)
    {
        string trimmed = publicAddress.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }
}
