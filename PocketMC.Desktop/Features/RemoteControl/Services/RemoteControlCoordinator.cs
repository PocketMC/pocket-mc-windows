using PocketMC.Desktop.Features.RemoteControl.Auth;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.RemoteControl.Services;

public sealed class RemoteControlCoordinator
{
    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteAuthService _authService;
    private readonly RemoteDashboardHost _dashboardHost;
    private readonly RemoteTunnelManager _tunnelManager;
    private readonly LocalNetworkAddressService _localNetworkAddressService;

    public RemoteControlCoordinator(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteAuthService authService,
        RemoteDashboardHost dashboardHost,
        RemoteTunnelManager tunnelManager,
        LocalNetworkAddressService localNetworkAddressService)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _authService = authService;
        _dashboardHost = dashboardHost;
        _tunnelManager = tunnelManager;
        _localNetworkAddressService = localNetworkAddressService;
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
            ActiveDeviceCount = settings.PairedDevices.Count(device => !device.RevokedAtUtc.HasValue),
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
        _applicationState.Settings.RemoteControl.Enabled = false;
        _settingsManager.Save(_applicationState.Settings);
        await _tunnelManager.StopAsync(cancellationToken);
        await _dashboardHost.StopAsync(cancellationToken);
    }

    public async Task RestartHostAsync(CancellationToken cancellationToken = default)
    {
        if (!_applicationState.Settings.RemoteControl.Enabled)
        {
            return;
        }

        await _dashboardHost.StopAsync(cancellationToken);
        await _dashboardHost.StartAsync(cancellationToken);
    }

    public async Task<RemoteTunnelStartResult> StartTunnelAsync(CancellationToken cancellationToken = default)
    {
        if (!_applicationState.Settings.RemoteControl.Enabled)
        {
            return RemoteTunnelStartResult.Failed("Remote Control is disabled.");
        }

        await _dashboardHost.StartAsync(cancellationToken);
        return await _tunnelManager.StartAsync(cancellationToken);
    }

    public Task StopTunnelAsync(CancellationToken cancellationToken = default) =>
        _tunnelManager.StopAsync(cancellationToken);

    public RemotePairingLink CreatePairingLink()
    {
        RemotePairingSession session = _authService.CreatePairingSession();
        string baseUrl = GetBestBaseUrl();
        string url = $"{baseUrl.TrimEnd('/')}/pair?token={Uri.EscapeDataString(session.Token)}";
        return new RemotePairingLink
        {
            Url = url,
            ExpiresAtUtc = session.ExpiresAtUtc
        };
    }

    public void RevokeAllDevices() => _authService.RevokeAllDevices();

    public void RevokeDevice(string deviceId) => _authService.RevokeDevice(deviceId);

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        await _tunnelManager.StopAsync(cancellationToken);
        await _dashboardHost.StopAsync(cancellationToken);
    }

    private string GetBestBaseUrl()
    {
        RemoteTunnelStatus tunnelStatus = _tunnelManager.GetStatus();
        if (!string.IsNullOrWhiteSpace(tunnelStatus.PublicUrl))
        {
            return tunnelStatus.PublicUrl;
        }

        return _localNetworkAddressService.GetPreferredLocalUrl(_applicationState.Settings.RemoteControl.Port);
    }
}
