using Microsoft.Extensions.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.RemoteControl.Hosting;

public sealed class RemoteControlHostedService : IHostedService
{
    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteDashboardHost _dashboardHost;
    private readonly RemoteControlCoordinator _coordinator;

    public RemoteControlHostedService(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteDashboardHost dashboardHost,
        RemoteControlCoordinator coordinator)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _dashboardHost = dashboardHost;
        _coordinator = coordinator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = _settingsManager.Load();
        if (settings.RemoteControl.Enabled && !string.IsNullOrWhiteSpace(settings.AppRootPath))
        {
            _applicationState.ApplySettings(settings);
            await _coordinator.StartTunnelAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _coordinator.StopAllAsync(cancellationToken);
}
