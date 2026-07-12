using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure.Tunnel;

namespace PocketMC.RemoteControl.Tunnels;

public sealed class PlayitHttpsTunnelProvider : IRemoteTunnelProvider
{
    private const string ProviderId = "playit-https";
    private const string ProviderDisplayName = "PlayIt Premium HTTPS";
    private const string DedicatedTunnelName = "pocketmc-remote-control";
    private const string PremiumRequiredMessage = "PlayIt HTTPS tunnels require PlayIt Premium.";
    private static readonly TimeSpan PublicUrlTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly PlayitApiClient _apiClient;
    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<PlayitHttpsTunnelProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();
    private string? _activeTunnelId;
    private string? _publicUrl;
    private string? _errorMessage;
    private DateTimeOffset? _startedAtUtc;
    private bool _isRunning;

    public PlayitHttpsTunnelProvider(
        PlayitApiClient apiClient,
        ApplicationState applicationState,
        SettingsManager settingsManager,
        ILogger<PlayitHttpsTunnelProvider> logger)
    {
        _apiClient = apiClient;
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _logger = logger;
    }

    public string Id => ProviderId;
    public string DisplayName => ProviderDisplayName;

    public async Task<RemoteTunnelStartResult> StartAsync(
        RemoteTunnelStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ClearStatusForStart();

            if (string.IsNullOrWhiteSpace(_apiClient.GetSecretKey()))
            {
                return SetStartError("PocketMC is not connected to a Playit agent yet.");
            }

            TunnelListResult listResult = await _apiClient.GetTunnelsAsync();
            if (!listResult.Success)
            {
                return SetStartError(listResult.ErrorMessage ?? "Could not list PlayIt tunnels.");
            }

            RemoteControlSettings settings = _applicationState.Settings.RemoteControl;
            TunnelData? tunnel = FindOwnedTunnel(listResult.Tunnels, settings.PlayitTunnelId, request.LocalPort);
            string? tunnelId = tunnel?.Id;

            if (tunnel == null)
            {
                TunnelCreateResult createResult = await _apiClient.CreateHttpTunnelAsync(DedicatedTunnelName, request.LocalPort);
                if (!createResult.Success)
                {
                    string message = createResult.RequiresPlayitPremium
                        ? PremiumRequiredMessage
                        : createResult.ErrorMessage ?? "Could not create the PlayIt HTTPS tunnel.";
                    return SetStartError(message);
                }

                tunnelId = createResult.TunnelId;
                if (string.IsNullOrWhiteSpace(tunnelId))
                {
                    return SetStartError("PlayIt created the HTTPS tunnel but did not return a tunnel ID.");
                }

                SaveTunnelId(tunnelId);
            }
            else
            {
                SaveTunnelId(tunnel.Id);
            }

            ReadyTunnelResult readyResult = await WaitForPublicUrlAsync(
                tunnelId,
                request.LocalPort,
                ensureConfigured: true,
                cancellationToken);

            if (readyResult.Tunnel == null)
            {
                return SetStartError(readyResult.ErrorMessage ?? "Timed out waiting for the PlayIt HTTPS tunnel URL.");
            }

            string? publicUrl = FormatPublicUrl(readyResult.Tunnel.PublicAddress);
            if (string.IsNullOrWhiteSpace(publicUrl))
            {
                return SetStartError("Timed out waiting for the PlayIt HTTPS tunnel URL.");
            }

            lock (_statusLock)
            {
                _activeTunnelId = readyResult.Tunnel.Id;
                _publicUrl = publicUrl;
                _errorMessage = null;
                _startedAtUtc = DateTimeOffset.UtcNow;
                _isRunning = true;
            }

            return new RemoteTunnelStartResult
            {
                Success = true,
                PublicUrl = publicUrl
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? tunnelId = _activeTunnelId ?? _applicationState.Settings.RemoteControl.PlayitTunnelId;
            if (string.IsNullOrWhiteSpace(tunnelId))
            {
                MarkStopped();
                return;
            }

            TunnelListResult listResult = await _apiClient.GetTunnelsAsync();
            if (!listResult.Success)
            {
                SetStopError(listResult.ErrorMessage ?? "Could not list PlayIt tunnels before stopping the remote link.");
                return;
            }

            TunnelData? tunnel = FindOwnedTunnel(listResult.Tunnels, tunnelId, null);
            if (tunnel == null)
            {
                SetStopError("Could not find the dedicated PocketMC Remote Control PlayIt HTTPS tunnel to disable.");
                return;
            }

            TunnelActionResult disableResult = await _apiClient.EnableTunnelAsync(tunnel.Id, enabled: false);
            if (!disableResult.Success)
            {
                SetStopError(disableResult.ErrorMessage ?? "Could not disable the PlayIt HTTPS tunnel.");
                return;
            }

            MarkStopped();
        }
        finally
        {
            _gate.Release();
        }
    }

    public RemoteTunnelStatus GetStatus()
    {
        lock (_statusLock)
        {
            return new RemoteTunnelStatus
            {
                IsRunning = _isRunning,
                PublicUrl = _publicUrl,
                ErrorMessage = _errorMessage,
                StartedAtUtc = _startedAtUtc
            };
        }
    }

    private async Task<ReadyTunnelResult> WaitForPublicUrlAsync(
        string? preferredTunnelId,
        int localPort,
        bool ensureConfigured,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(PublicUrlTimeout);
        bool configured = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TunnelListResult listResult = await _apiClient.GetTunnelsAsync();
            if (!listResult.Success)
            {
                return new ReadyTunnelResult(null, listResult.ErrorMessage ?? "Could not list PlayIt tunnels.");
            }

            TunnelData? tunnel = FindOwnedTunnel(listResult.Tunnels, preferredTunnelId, localPort);
            if (tunnel != null)
            {
                SaveTunnelId(tunnel.Id);

                if (ensureConfigured && !configured)
                {
                    TunnelActionResult configureResult = await ConfigureTunnelAsync(tunnel.Id, localPort);
                    if (!configureResult.Success)
                    {
                        return new ReadyTunnelResult(
                            null,
                            configureResult.ErrorMessage ?? "Could not update the PlayIt HTTPS tunnel target.");
                    }

                    configured = true;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(tunnel.PublicAddress))
                {
                    return new ReadyTunnelResult(tunnel, null);
                }
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new ReadyTunnelResult(null, "Timed out waiting for the PlayIt HTTPS tunnel URL.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<TunnelActionResult> ConfigureTunnelAsync(string tunnelId, int localPort)
    {
        TunnelActionResult updateResult = await _apiClient.UpdateTunnelAsync(
            tunnelId,
            "127.0.0.1",
            localPort,
            _apiClient.GetAgentId(),
            enabled: true);
        if (!updateResult.Success)
        {
            return updateResult;
        }

        return await _apiClient.EnableTunnelAsync(tunnelId, enabled: true);
    }

    private static TunnelData? FindOwnedTunnel(IEnumerable<TunnelData> tunnels, string? savedTunnelId, int? localPort)
    {
        List<TunnelData> tunnelList = tunnels.ToList();
        if (!string.IsNullOrWhiteSpace(savedTunnelId))
        {
            TunnelData? byId = tunnelList.FirstOrDefault(tunnel =>
                string.Equals(tunnel.Id, savedTunnelId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(tunnel.TunnelType, "https", StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        if (localPort.HasValue)
        {
            TunnelData? byPort = tunnelList.FirstOrDefault(tunnel =>
                string.Equals(tunnel.TunnelType, "https", StringComparison.OrdinalIgnoreCase) &&
                tunnel.Port == localPort.Value);
            
            if (byPort != null)
            {
                return byPort;
            }
        }

        return tunnelList.FirstOrDefault(tunnel =>
            string.Equals(tunnel.Name, DedicatedTunnelName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(tunnel.TunnelType, "https", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FormatPublicUrl(string? publicAddress)
    {
        if (string.IsNullOrWhiteSpace(publicAddress))
        {
            return null;
        }

        string trimmed = publicAddress.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return trimmed;
        }

        return $"https://{trimmed.TrimEnd('/')}";
    }

    private void SaveTunnelId(string tunnelId)
    {
        if (string.IsNullOrWhiteSpace(tunnelId))
        {
            return;
        }

        if (string.Equals(_applicationState.Settings.RemoteControl.PlayitTunnelId, tunnelId, StringComparison.Ordinal))
        {
            return;
        }

        _applicationState.Settings.RemoteControl.PlayitTunnelId = tunnelId;
        try
        {
            _settingsManager.Save(_applicationState.Settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save the PlayIt Remote Control tunnel ID.");
        }
    }

    private void ClearStatusForStart()
    {
        lock (_statusLock)
        {
            _activeTunnelId = null;
            _errorMessage = null;
            _publicUrl = null;
            _startedAtUtc = null;
            _isRunning = false;
        }
    }

    private RemoteTunnelStartResult SetStartError(string message)
    {
        lock (_statusLock)
        {
            _activeTunnelId = null;
            _errorMessage = message;
            _publicUrl = null;
            _startedAtUtc = null;
            _isRunning = false;
        }

        return RemoteTunnelStartResult.Failed(message);
    }

    private void SetStopError(string message)
    {
        lock (_statusLock)
        {
            _errorMessage = message;
            _isRunning = !string.IsNullOrWhiteSpace(_activeTunnelId) ||
                         !string.IsNullOrWhiteSpace(_publicUrl);
        }
    }

    private void MarkStopped()
    {
        lock (_statusLock)
        {
            _activeTunnelId = null;
            _errorMessage = null;
            _publicUrl = null;
            _startedAtUtc = null;
            _isRunning = false;
        }
    }

    private sealed record ReadyTunnelResult(TunnelData? Tunnel, string? ErrorMessage);
}

