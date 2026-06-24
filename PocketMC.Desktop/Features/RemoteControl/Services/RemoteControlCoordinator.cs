using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.RemoteControl.Services;

public sealed class RemoteControlCoordinator
{
    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteDashboardHost _dashboardHost;
    private readonly RemoteTunnelManager _tunnelManager;
    private readonly LocalNetworkAddressService _localNetworkAddressService;
    private readonly Infrastructure.WindowsToastNotificationService? _toastNotificationService;
    private string? _lastNotifiedTunnelUrl;

    public RemoteControlCoordinator(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteDashboardHost dashboardHost,
        RemoteTunnelManager tunnelManager,
        LocalNetworkAddressService localNetworkAddressService,
        Infrastructure.WindowsToastNotificationService? toastNotificationService = null)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _dashboardHost = dashboardHost;
        _tunnelManager = tunnelManager;
        _localNetworkAddressService = localNetworkAddressService;
        _toastNotificationService = toastNotificationService;
    }

    public RemoteDashboardStatus GetStatus()
    {
        RemoteControlSettings settings = _applicationState.Settings.RemoteControl;
        RemoteTunnelStatus tunnelStatus = _tunnelManager.GetStatus();
        return new RemoteDashboardStatus
        {
            Enabled = settings.Enabled,
            HostRunning = _dashboardHost.IsRunning,
            Port = settings.Port,
            AccessMode = settings.AccessMode,
            LocalUrls = _localNetworkAddressService.GetLocalUrls(settings.Port),
            PublicUrl = tunnelStatus.PublicUrl,
            TunnelRunning = tunnelStatus.IsRunning,
            TunnelError = tunnelStatus.ErrorMessage,
            AllowRemoteConsoleCommands = settings.AllowRemoteConsoleCommands,
            AllowRemotePlayerActions = settings.AllowRemotePlayerActions
        };
    }

    public async Task StartHostAsync(CancellationToken cancellationToken = default)
    {
        var settings = _applicationState.Settings;
        bool previousEnabled = settings.RemoteControl.Enabled;

        settings.RemoteControl.Enabled = true;

        try
        {
            await _dashboardHost.StartAsync(cancellationToken);
            _settingsManager.Save(settings);
        }
        catch
        {
            settings.RemoteControl.Enabled = previousEnabled;
            _settingsManager.Save(settings);
            throw;
        }
    }

    public async Task StopHostAsync(CancellationToken cancellationToken = default)
    {
        _lastNotifiedTunnelUrl = null;
        _applicationState.Settings.RemoteControl.Enabled = false;
        _settingsManager.Save(_applicationState.Settings);
        await _tunnelManager.StopAsync(cancellationToken);
        await _dashboardHost.StopAsync(cancellationToken);
    }

    public async Task RestartAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_applicationState.Settings.RemoteControl.Enabled)
        {
            return;
        }

        await StopAllAsync(cancellationToken);
        await StartTunnelAsync(cancellationToken);
    }

    public async Task<RemoteTunnelStartResult> StartTunnelAsync(CancellationToken cancellationToken = default)
    {
        if (!_applicationState.Settings.RemoteControl.Enabled)
        {
            return RemoteTunnelStartResult.Failed("Remote Control is disabled.");
        }

        await _dashboardHost.StartAsync(cancellationToken);
        var result = await _tunnelManager.StartAsync(cancellationToken);

        if (result.Success && !string.IsNullOrEmpty(result.PublicUrl))
        {
            _ = NotifyDiscordOfRemoteControlUrlAsync(result.PublicUrl);

            if (_applicationState.Settings.EnableRemoteControlNotifications &&
                _lastNotifiedTunnelUrl != result.PublicUrl &&
                _toastNotificationService != null)
            {
                _lastNotifiedTunnelUrl = result.PublicUrl;
                _toastNotificationService.ShowRemoteControlStarted();
            }
        }

        return result;
    }

    private string? _lastNotifiedUrl;

    private async Task NotifyDiscordOfRemoteControlUrlAsync(string publicUrl)
    {
        var settings = _applicationState.Settings;
        if (!settings.EnableDiscordNotifications || string.IsNullOrEmpty(settings.DiscordUserId) || string.IsNullOrEmpty(settings.DiscordApiUrl) || string.IsNullOrEmpty(settings.DiscordApiKey))
        {
            return;
        }

        if (_lastNotifiedUrl == publicUrl)
        {
            return; // Avoid duplicate DMs
        }

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.DiscordApiKey);

            var payload = new
            {
                user_id = settings.DiscordUserId,
                message = $"Hey! Your PocketMC Remote Control Dashboard is now online at: {publicUrl}"
            };

            var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{settings.DiscordApiUrl.TrimEnd('/')}/send-dm", content);
            if (response.IsSuccessStatusCode)
            {
                _lastNotifiedUrl = publicUrl;
            }
        }
        catch (Exception)
        {
            // Ignore failure
        }
    }

    public Task StopTunnelAsync(CancellationToken cancellationToken = default)
    {
        _lastNotifiedTunnelUrl = null;
        return _tunnelManager.StopAsync(cancellationToken);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _lastNotifiedTunnelUrl = null;
        await _tunnelManager.StopAsync(cancellationToken);
        await _dashboardHost.StopAsync(cancellationToken);
    }
}

