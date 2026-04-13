using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Features.Tunnel
{
    /// <summary>
    /// Orchestrates the network tunnel flow for server instances, including
    /// resolution, guide navigation, and agent health checks.
    /// Extracts this logic from DashboardViewModel to reduce coupling.
    /// </summary>
    public class InstanceTunnelOrchestrator
    {
        private readonly TunnelService _tunnelService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly ApplicationState _applicationState;
        private readonly ServerConfigurationService _serverConfigurationService;
        private readonly InstanceManager _instanceManager;
        private readonly InstanceRegistry _registry;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<InstanceTunnelOrchestrator> _logger;

        private readonly HashSet<Guid> _resolutionsInFlight = new();
        private readonly object _lock = new();

        public InstanceTunnelOrchestrator(
            TunnelService tunnelService,
            PlayitAgentService playitAgentService,
            ApplicationState applicationState,
            ServerConfigurationService serverConfigurationService,
            InstanceManager instanceManager,
            InstanceRegistry registry,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider,
            ILogger<InstanceTunnelOrchestrator> logger)
        {
            _tunnelService = tunnelService;
            _playitAgentService = playitAgentService;
            _applicationState = applicationState;
            _serverConfigurationService = serverConfigurationService;
            _instanceManager = instanceManager;
            _registry = registry;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task EnsureTunnelFlowAsync(Guid instanceId, string instanceName, Action<string?> onAddressResolved)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()))
            {
                return;
            }

            lock (_lock)
            {
                if (!_resolutionsInFlight.Add(instanceId))
                {
                    return;
                }
            }

            try
            {
                if (!TryGetServerPort(instanceId, out int serverPort))
                {
                    _logger.LogDebug("Skipping tunnel resolution for {InstanceName} because the server port could not be read.", instanceName);
                    return;
                }

                _dispatcher.Invoke(() => onAddressResolved(null));
                EnsurePlayitAgentRunning();

                TunnelResolutionResult resolution = await ResolveTunnelWithWarmupAsync(serverPort);
                switch (resolution.Status)
                {
                    case TunnelResolutionResult.TunnelStatus.Found:
                        if (!string.IsNullOrWhiteSpace(resolution.PublicAddress))
                        {
                            _applicationState.SetTunnelAddress(instanceId, resolution.PublicAddress!);
                            _dispatcher.Invoke(() => onAddressResolved(resolution.PublicAddress));
                        }
                        break;

                    case TunnelResolutionResult.TunnelStatus.CreationStarted:
                        _dispatcher.Invoke(() =>
                        {
                            var guidePage = ActivatorUtilities.CreateInstance<TunnelCreationGuidePage>(_serviceProvider, serverPort);
                            guidePage.OnTunnelResolved += address =>
                            {
                                if (!string.IsNullOrWhiteSpace(address))
                                {
                                    _applicationState.SetTunnelAddress(instanceId, address);
                                }
                                _dispatcher.Invoke(() => onAddressResolved(address));
                            };

                            _navigationService.NavigateToDetailPage(
                                guidePage,
                                $"Tunnel Setup: {instanceName}",
                                DetailRouteKind.TunnelCreationGuide,
                                DetailBackNavigation.Dashboard,
                                clearDetailStack: true);
                        });
                        break;

                    case TunnelResolutionResult.TunnelStatus.LimitReached:
                        _dispatcher.Invoke(() =>
                            _dialogService.ShowMessage(
                                "Tunnel Limit Reached",
                                "Your Playit account already has 4 tunnels. Delete one in Playit or change this server's port, then try again.",
                                DialogType.Warning));
                        break;

                    case TunnelResolutionResult.TunnelStatus.AgentOffline:
                        _logger.LogInformation("Playit agent is not ready yet for instance {InstanceName}.", instanceName);
                        break;

                    case TunnelResolutionResult.TunnelStatus.Error:
                        HandleResolutionError(instanceName, resolution);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to complete the Playit tunnel flow for {InstanceName}.", instanceName);
            }
            finally
            {
                lock (_lock)
                {
                    _resolutionsInFlight.Remove(instanceId);
                }
            }
        }

        private async Task<TunnelResolutionResult> ResolveTunnelWithWarmupAsync(int serverPort)
        {
            TunnelResolutionResult? lastResult = null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                lastResult = await _tunnelService.ResolveTunnelAsync(serverPort);
                bool shouldRetry =
                    attempt < 3 &&
                    (lastResult.Status == TunnelResolutionResult.TunnelStatus.AgentOffline ||
                     (lastResult.Status == TunnelResolutionResult.TunnelStatus.Error && lastResult.RequiresClaim));

                if (!shouldRetry)
                {
                    return lastResult;
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            return lastResult ?? new TunnelResolutionResult
            {
                Status = TunnelResolutionResult.TunnelStatus.Error,
                ErrorMessage = "Tunnel resolution did not complete."
            };
        }

        private void HandleResolutionError(string instanceName, TunnelResolutionResult resolution)
        {
            if (resolution.RequiresClaim)
            {
                _logger.LogInformation("Playit claim is still pending for instance {InstanceName}.", instanceName);
            }
            else if (resolution.IsTokenInvalid)
            {
                _dispatcher.Invoke(() =>
                    _dialogService.ShowMessage(
                        "Playit Reconnect Required",
                        "PocketMC detected that your Playit agent needs to be linked again. Open the Tunnel page and click Reconnect.",
                        DialogType.Warning));
            }
            else if (!string.IsNullOrWhiteSpace(resolution.ErrorMessage))
            {
                _logger.LogWarning("Playit tunnel resolution failed for {InstanceName}: {Message}", instanceName, resolution.ErrorMessage);
            }
        }

        private void EnsurePlayitAgentRunning()
        {
            if (_playitAgentService.IsRunning) return;
            if (_playitAgentService.State is PlayitAgentState.WaitingForClaim or PlayitAgentState.Starting) return;
            _playitAgentService.Start();
        }

        private bool TryGetServerPort(Guid instanceId, out int serverPort)
        {
            serverPort = 25565; // Default Minecraft port
            string? instancePath = _registry.GetPath(instanceId);
            if (string.IsNullOrWhiteSpace(instancePath)) return false;

            if (_serverConfigurationService.TryGetProperty(instancePath, "server-port", out string? portString) &&
                int.TryParse(portString, out int parsedPort))
            {
                serverPort = parsedPort;
                return true;
            }

            // Return true even if missing, as we'll use the default 25565
            return true;
        }
    }
}
