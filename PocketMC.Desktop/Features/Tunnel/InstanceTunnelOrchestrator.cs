using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Infrastructure;

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
        private readonly PortPreflightService _portPreflightService;
        private readonly PortFailureMessageService _portFailureMessageService;
        private readonly PortRecoveryService _portRecoveryService;
        private readonly InstanceRegistry _registry;
        private readonly IDialogService _dialogService;
        private readonly IAppDispatcher _dispatcher;
        private readonly ILogger<InstanceTunnelOrchestrator> _logger;

        private readonly HashSet<Guid> _resolutionsInFlight = new();
        private readonly object _lock = new();

        public InstanceTunnelOrchestrator(
            TunnelService tunnelService,
            PlayitAgentService playitAgentService,
            ApplicationState applicationState,
            PortPreflightService portPreflightService,
            PortFailureMessageService portFailureMessageService,
            PortRecoveryService portRecoveryService,
            InstanceRegistry registry,
            IDialogService dialogService,
            IAppDispatcher dispatcher,
            ILogger<InstanceTunnelOrchestrator> logger)
        {
            _tunnelService = tunnelService;
            _playitAgentService = playitAgentService;
            _applicationState = applicationState;
            _portPreflightService = portPreflightService;
            _portFailureMessageService = portFailureMessageService;
            _portRecoveryService = portRecoveryService;
            _registry = registry;
            _dialogService = dialogService;
            _dispatcher = dispatcher;
            _logger = logger;
        }

        public async Task EnsureTunnelFlowAsync(InstanceCardViewModel vm)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()))
            {
                return;
            }

            lock (_lock)
            {
                if (!_resolutionsInFlight.Add(vm.Id))
                {
                    return;
                }
            }

            try
            {
                IReadOnlyList<PortCheckRequest> requests = BuildTunnelRequests(vm);
                if (requests.Count == 0)
                {
                    _logger.LogDebug("Skipping tunnel resolution for {InstanceName} because no tunnel-relevant ports could be resolved.", vm.Name);
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    vm.SetTunnelResolving(true);
                    vm.TunnelAddress = null;
                    vm.BedrockTunnelAddress = null;
                });
                EnsurePlayitAgentRunning();

                foreach (PortCheckRequest request in requests)
                {
                    if (IsGeyserBedrockRequest(request))
                    {
                        _dispatcher.Invoke(() => vm.SetBedrockLocalPort(request.Port));
                    }

                    bool isBedrockTunnel = IsBedrockTunnelRequest(request);
                    TunnelResolutionResult resolution = await ResolveTunnelWithWarmupAsync(request);

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.Found)
                    {
                        if (!string.IsNullOrWhiteSpace(resolution.PublicAddress))
                        {
                            SetTunnelAddress(vm, request, resolution.PublicAddress, resolution.NumericAddress);
                        }
                        continue;
                    }

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.AutoCreated)
                    {
                        if (!string.IsNullOrWhiteSpace(resolution.PublicAddress))
                        {
                            SetTunnelAddress(vm, request, resolution.PublicAddress, resolution.NumericAddress);
                        }
                        continue;
                    }

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.LimitReached)
                    {
                        _dispatcher.Invoke(() =>
                        {
                            vm.SetTunnelError("Address unavailable");
                            if (!string.IsNullOrEmpty(resolution.CreateErrorCode))
                            {
                                AppDialog.ShowError("Tunnel Creation Failed",
                                    TunnelCreateResult.MapCreateError(resolution.CreateErrorCode));
                            }
                        });
                        break;
                    }
                    else if (resolution.Status == TunnelResolutionResult.TunnelStatus.AgentOffline)
                    {
                        PortCheckResult? result = resolution.ToPortCheckResult(request);
                        if (result != null)
                        {
                            _portRecoveryService.Recommend(result);
                        }

                        _logger.LogInformation("Playit agent is not ready yet for instance {InstanceName}.", vm.Name);
                        break;
                    }
                    else if (resolution.Status == TunnelResolutionResult.TunnelStatus.Error)
                    {
                        _dispatcher.Invoke(() =>
                        {
                            vm.SetTunnelError("Address unavailable");
                            if (!string.IsNullOrEmpty(resolution.CreateErrorCode))
                            {
                                AppDialog.ShowError("Tunnel Creation Failed",
                                    TunnelCreateResult.MapCreateError(resolution.CreateErrorCode));
                            }
                        });
                        HandleResolutionError(vm.Name, request, resolution);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to complete the Playit tunnel flow for {InstanceName}.", vm.Name);
            }
            finally
            {
                _dispatcher.Invoke(() => vm.SetTunnelResolving(false));
                lock (_lock)
                {
                    _resolutionsInFlight.Remove(vm.Id);
                }
            }
        }

        private void SetTunnelAddress(InstanceCardViewModel vm, PortCheckRequest request, string address, string? numericAddress)
        {
            _dispatcher.Invoke(() =>
            {
                if (IsGeyserBedrockRequest(request))
                {
                    _applicationState.SetBedrockTunnelAddress(vm.Id, address);
                    vm.BedrockTunnelAddress = address;
                    vm.BedrockNumericTunnelAddress = numericAddress;
                    if (numericAddress != null)
                    {
                        _applicationState.SetBedrockNumericTunnelAddress(vm.Id, numericAddress);
                    }
                }
                else
                {
                    _applicationState.SetTunnelAddress(vm.Id, address);
                    vm.TunnelAddress = address;
                    vm.NumericTunnelAddress = numericAddress;
                    if (numericAddress != null)
                    {
                        _applicationState.SetNumericTunnelAddress(vm.Id, numericAddress);
                    }
                }
            });
        }

        private async Task<TunnelResolutionResult> ResolveTunnelWithWarmupAsync(PortCheckRequest request)
        {
            TunnelResolutionResult? lastResult = null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                lastResult = await _tunnelService.ResolveTunnelAsync(request);
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

        private void HandleResolutionError(string instanceName, PortCheckRequest request, TunnelResolutionResult resolution)
        {
            if (resolution.RequiresClaim)
            {
                PortCheckResult? result = resolution.ToPortCheckResult(request);
                if (result != null)
                {
                    _portRecoveryService.Recommend(result);
                }

                _logger.LogInformation("Playit needs to be connected before tunnel resolution can continue for {InstanceName}.", instanceName);
            }
            else if (resolution.IsTokenInvalid)
            {
                ShowTunnelFailure(instanceName, request, resolution);
            }
            else if (!string.IsNullOrWhiteSpace(resolution.ErrorMessage))
            {
                PortCheckResult? result = resolution.ToPortCheckResult(request);
                if (result != null)
                {
                    _portRecoveryService.Recommend(result);
                }

                _logger.LogWarning(
                    "Playit tunnel resolution failed for {InstanceName}: Code={FailureCode}, Port={Port}, Protocol={Protocol}, Engine={Engine}, Message={Message}",
                    instanceName,
                    result?.FailureCode ?? PortFailureCode.PublicReachabilityFailure,
                    request.Port,
                    request.Protocol,
                    request.Engine,
                    resolution.ErrorMessage);
            }
        }

        private void ShowTunnelFailure(string instanceName, PortCheckRequest request, TunnelResolutionResult resolution)
        {
            PortCheckResult? result = resolution.ToPortCheckResult(request);
            if (result == null)
            {
                return;
            }

            PortRecoveryRecommendation recommendation = _portRecoveryService.Recommend(result);
            result = new PortCheckResult(
                result.Request,
                result.IsSuccessful,
                result.CanBindLocally,
                result.FailureCode,
                result.FailureMessage,
                result.Lease,
                result.Conflicts,
                new[] { recommendation },
                result.CheckedAtUtc);

            var exception = new PortReliabilityException(new[] { result });
            PortFailureDisplayInfo display = _portFailureMessageService.CreateDisplayInfo(exception, instanceName);

            _dispatcher.Invoke(() =>
                _dialogService.ShowMessage(
                    display.Title,
                    display.Message,
                    DialogType.Warning));
        }

        private void EnsurePlayitAgentRunning()
        {
            if (_playitAgentService.IsRunning) return;
            if (_playitAgentService.State is PlayitAgentState.AwaitingSetupCode or PlayitAgentState.ProvisioningAgent or PlayitAgentState.Starting) return;
            _playitAgentService.Start();
        }

        private IReadOnlyList<PortCheckRequest> BuildTunnelRequests(InstanceCardViewModel vm)
        {
            InstanceMetadata? metadata = _registry.GetById(vm.Id);
            if (metadata == null)
            {
                return Array.Empty<PortCheckRequest>();
            }

            string? instancePath = _registry.GetPath(vm.Id);
            IReadOnlyList<PortCheckRequest> requests;
            try
            {
                requests = _portPreflightService.BuildRequests(metadata, instancePath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not build tunnel port requests for {InstanceName}.", vm.Name);
                return Array.Empty<PortCheckRequest>();
            }

            return requests
                .Where(IsTunnelRelevantRequest)
                .GroupBy(request => new
                {
                    request.Port,
                    request.Protocol,
                    request.BindingRole,
                    request.Engine
                })
                .Select(group => group.First())
                .ToArray();
        }

        private static bool IsTunnelRelevantRequest(PortCheckRequest request)
        {
            return request.IpMode != PortIpMode.IPv6;
        }

        private static bool IsGeyserBedrockRequest(PortCheckRequest request)
        {
            return request.BindingRole == PortBindingRole.GeyserBedrock ||
                   request.Engine == PortEngine.Geyser;
        }

        private static bool IsBedrockTunnelRequest(PortCheckRequest request)
        {
            return request.Protocol == PortProtocol.Udp ||
                   request.BindingRole is PortBindingRole.BedrockServer or PortBindingRole.PocketMineServer or PortBindingRole.GeyserBedrock ||
                   request.Engine is PortEngine.BedrockDedicated or PortEngine.PocketMine or PortEngine.Geyser;
        }
    }
}
