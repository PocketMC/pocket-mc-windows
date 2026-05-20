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
        private readonly InstanceManager _instanceManager;
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
            InstanceManager instanceManager,
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
            _instanceManager = instanceManager;
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
                });
                EnsurePlayitAgentRunning();

                foreach (PortCheckRequest request in requests)
                {
                    if (IsSimpleVoiceChatRequest(request))
                    {
                        continue;
                    }

                    if (IsGeyserBedrockRequest(request))
                    {
                        _dispatcher.Invoke(() => vm.SetBedrockLocalPort(request.Port));
                    }

                    TunnelResolutionResult resolution = await ResolveTunnelWithWarmupAsync(request);

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.Found)
                    {
                        if (!string.IsNullOrWhiteSpace(resolution.PublicAddress))
                        {
                            SetTunnelAddress(vm, request, resolution.PublicAddress, resolution.NumericAddress, resolution.TunnelId);
                            _dispatcher.Invoke(() => vm.ClearPortIssue());
                        }
                        else
                        {
                            _dispatcher.Invoke(() => vm.SetTunnelError("Playit tunnel found, waiting for public address..."));
                        }
                        continue;
                    }

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.AutoCreated)
                    {
                        if (!string.IsNullOrWhiteSpace(resolution.PublicAddress))
                        {
                            SetTunnelAddress(vm, request, resolution.PublicAddress, resolution.NumericAddress, resolution.TunnelId);
                            _dispatcher.Invoke(() => vm.ClearPortIssue());
                        }
                        else
                        {
                            _dispatcher.Invoke(() => vm.SetTunnelError(resolution.ErrorMessage ?? "Address pending"));
                        }
                        continue;
                    }

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.FoundPendingAllocation)
                    {
                        _dispatcher.Invoke(() =>
                            vm.SetTunnelError(resolution.ErrorMessage ?? "Playit tunnel found, waiting for public address..."));
                        HandleResolutionError(vm.Name, request, resolution);
                        continue;
                    }

                    if (resolution.Status == TunnelResolutionResult.TunnelStatus.LimitReached)
                    {
                        _dispatcher.Invoke(() =>
                        {
                            vm.SetTunnelError(resolution.ErrorMessage ?? "Address unavailable");
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
                        _dispatcher.Invoke(() =>
                            vm.SetTunnelError(resolution.ErrorMessage ?? "Playit agent is not connected."));
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
                            vm.SetTunnelError(resolution.ErrorMessage ?? "Address unavailable");
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

        public async Task<bool> EnsureSimpleVoiceChatBeforeStartAsync(InstanceCardViewModel vm, bool isBeforeLaunch = false)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()))
            {
                return true;
            }

            InstanceMetadata? metadata = _registry.GetById(vm.Id);
            string? instancePath = _registry.GetPath(vm.Id);
            if (metadata == null || string.IsNullOrWhiteSpace(instancePath))
            {
                return true;
            }

            SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(instancePath);
            if (!detection.IsDetected)
            {
                return true;
            }

            bool applyBeforeStart = isBeforeLaunch || !vm.IsRunning;
            PortCheckRequest request = BuildSimpleVoiceChatRequest(metadata, instancePath, detection);
            TunnelResolutionResult existing = await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: false);
            if (existing.Status == TunnelResolutionResult.TunnelStatus.Found &&
                !string.IsNullOrWhiteSpace(existing.PublicAddress))
            {
                ApplySimpleVoiceChatTunnel(vm, request, existing, isBeforeStart: applyBeforeStart);
                return true;
            }

            if (metadata.SimpleVoiceChatPromptDismissed)
            {
                SetSimpleVoiceChatWarning(
                    vm,
                    "Simple Voice Chat is installed but no Playit tunnel is configured. Remote players may see voice chat as disconnected.",
                    status: "StartedWithoutVoiceTunnel");
                return true;
            }

            DialogResult choice = await _dialogService.ShowDialogAsync(
                "Voice Chat Tunnel Required",
                BuildSimpleVoiceChatPromptMessage(vm, request, existing),
                DialogType.Warning,
                showCancel: true,
                primaryButtonText: "Create Tunnel",
                secondaryButtonText: "Skip",
                cancelButtonText: "Don't ask again");

            if (choice == DialogResult.Yes)
            {
                TunnelResolutionResult created = await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: true);
                if ((created.Status == TunnelResolutionResult.TunnelStatus.Found ||
                     created.Status == TunnelResolutionResult.TunnelStatus.AutoCreated) &&
                    !string.IsNullOrWhiteSpace(created.PublicAddress))
                {
                    ApplySimpleVoiceChatTunnel(vm, request, created, isBeforeStart: applyBeforeStart);
                    return true;
                }

                SetSimpleVoiceChatWarning(vm, created.ErrorMessage ?? "Simple Voice Chat tunnel missing", status: "TunnelIssue");
                return true;
            }

            if (choice == DialogResult.Cancel)
            {
                metadata.SimpleVoiceChatPromptDismissed = true;
            }

            SetSimpleVoiceChatWarning(
                vm,
                "Simple Voice Chat is installed but no Playit tunnel is configured. Remote players may see voice chat as disconnected.",
                status: "StartedWithoutVoiceTunnel");
            return true;
        }

        public async Task<bool> EnsureSimpleVoiceChatBeforeStartAsync(InstanceMetadata metadata)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()))
            {
                return true;
            }

            string? instancePath = _registry.GetPath(metadata.Id);
            if (string.IsNullOrWhiteSpace(instancePath))
            {
                return true;
            }

            SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(instancePath);
            if (!detection.IsDetected)
            {
                return true;
            }

            PortCheckRequest request = BuildSimpleVoiceChatRequest(metadata, instancePath, detection);
            TunnelResolutionResult existing = await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: false);
            if (existing.Status == TunnelResolutionResult.TunnelStatus.Found &&
                !string.IsNullOrWhiteSpace(existing.PublicAddress))
            {
                ApplySimpleVoiceChatTunnel(metadata, request, existing, isBeforeStart: true);
                return true;
            }

            if (metadata.SimpleVoiceChatPromptDismissed)
            {
                SaveSimpleVoiceChatMetadata(
                    metadata,
                    request,
                    "Simple Voice Chat is installed but no Playit tunnel is configured. Remote players may see voice chat as disconnected.",
                    "StartedWithoutVoiceTunnel");
                return true;
            }

            DialogResult choice = await _dialogService.ShowDialogAsync(
                "Voice Chat Tunnel Required",
                BuildSimpleVoiceChatPromptMessage(request, existing),
                DialogType.Warning,
                showCancel: true,
                primaryButtonText: "Create Tunnel",
                secondaryButtonText: "Skip",
                cancelButtonText: "Don't ask again");

            if (choice == DialogResult.Yes)
            {
                TunnelResolutionResult created = await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: true);
                if ((created.Status == TunnelResolutionResult.TunnelStatus.Found ||
                     created.Status == TunnelResolutionResult.TunnelStatus.AutoCreated) &&
                    !string.IsNullOrWhiteSpace(created.PublicAddress))
                {
                    ApplySimpleVoiceChatTunnel(metadata, request, created, isBeforeStart: true);
                    return true;
                }

                SaveSimpleVoiceChatMetadata(metadata, request, created.ErrorMessage ?? "Simple Voice Chat tunnel missing", "TunnelIssue");
                return true;
            }

            if (choice == DialogResult.Cancel)
            {
                metadata.SimpleVoiceChatPromptDismissed = true;
            }

            SaveSimpleVoiceChatMetadata(
                metadata,
                request,
                "Simple Voice Chat is installed but no Playit tunnel is configured. Remote players may see voice chat as disconnected.",
                "StartedWithoutVoiceTunnel");
            return true;
        }

        private void SetTunnelAddress(InstanceCardViewModel vm, PortCheckRequest request, string address, string? numericAddress, string? tunnelId)
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
                else if (IsSimpleVoiceChatRequest(request))
                {
                    bool restartRequired = false;
                    _applicationState.SetVoiceChatTunnelAddress(vm.Id, address);
                    vm.VoiceChatTunnelAddress = address;
                    vm.VoiceChatNumericTunnelAddress = numericAddress;
                    if (numericAddress != null)
                    {
                        _applicationState.SetVoiceChatNumericTunnelAddress(vm.Id, numericAddress);
                    }

                    if (!string.IsNullOrWhiteSpace(request.InstancePath))
                    {
                        bool patched = SimpleVoiceChatConfigService.TryPatchVoiceHost(request.InstancePath, address);
                        restartRequired = patched && vm.IsRunning;
                    }

                    vm.ClearSimpleVoiceChatWarning();
                    if (restartRequired)
                    {
                        vm.SetSimpleVoiceChatWarning("Restart required for Simple Voice Chat tunnel changes to apply.");
                    }

                    vm.Metadata.SimpleVoiceChatDetected = true;
                    vm.Metadata.SimpleVoiceChatPort = request.Port;
                    vm.Metadata.SimpleVoiceChatTunnelId = tunnelId;
                    vm.Metadata.SimpleVoiceChatTunnelAddress = address;
                    vm.Metadata.SimpleVoiceChatNumericTunnelAddress = numericAddress;
                    vm.Metadata.SimpleVoiceChatLastWarning = restartRequired
                        ? "Restart required for Simple Voice Chat tunnel changes to apply."
                        : null;
                    SaveSimpleVoiceChatMetadata(
                        vm.Metadata,
                        request,
                        vm.Metadata.SimpleVoiceChatLastWarning,
                        restartRequired ? "RestartRequired" : "TunnelActive");
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
            return await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: true);
        }

        private async Task<TunnelResolutionResult> ResolveTunnelWithWarmupAsync(PortCheckRequest request, bool allowAutoCreate)
        {
            TunnelResolutionResult? lastResult = null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                lastResult = await _tunnelService.ResolveTunnelAsync(request, allowAutoCreate);
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

        private async Task<TunnelResolutionResult> ResolveSimpleVoiceChatTunnelAsync(InstanceCardViewModel vm, PortCheckRequest request)
        {
            TunnelResolutionResult existing = await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: false);
            if (existing.Status == TunnelResolutionResult.TunnelStatus.Found)
            {
                return existing;
            }

            if (vm.Metadata.SimpleVoiceChatPromptDismissed)
            {
                SetSimpleVoiceChatWarning(vm, "Simple Voice Chat tunnel missing");
                return existing;
            }

            DialogResult choice = await _dialogService.ShowDialogAsync(
                "Voice Chat Tunnel Required",
                BuildSimpleVoiceChatPromptMessage(vm, request, existing),
                DialogType.Warning,
                showCancel: true,
                primaryButtonText: "Create Tunnel",
                secondaryButtonText: "Skip",
                cancelButtonText: "Don't ask again");

            if (choice == DialogResult.Yes)
            {
                return await ResolveTunnelWithWarmupAsync(request, allowAutoCreate: true);
            }

            SetSimpleVoiceChatWarning(vm, "Simple Voice Chat tunnel missing");
            if (choice == DialogResult.Cancel)
            {
                vm.Metadata.SimpleVoiceChatPromptDismissed = true;
                SaveSimpleVoiceChatMetadata(vm.Metadata, request, "Simple Voice Chat tunnel missing", "StartedWithoutVoiceTunnel");
            }

            return existing;
        }

        private string BuildSimpleVoiceChatPromptMessage(InstanceCardViewModel vm, PortCheckRequest request, TunnelResolutionResult resolution)
        {
            return BuildSimpleVoiceChatPromptMessage(request, resolution);
        }

        private string BuildSimpleVoiceChatPromptMessage(PortCheckRequest request, TunnelResolutionResult resolution)
        {
            SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(request.InstancePath);
            string voiceHost = string.IsNullOrWhiteSpace(detection.VoiceHost) ? "(empty)" : detection.VoiceHost!;
            string agentStatus = _playitAgentService.State.ToString();
            string tunnelState = string.IsNullOrWhiteSpace(resolution.ErrorMessage)
                ? string.Empty
                : Environment.NewLine + $"Playit tunnel state: {resolution.ErrorMessage}";

            return
                "Simple Voice Chat requires its own tunnel. Without it, players can join but voice chat will appear disconnected." +
                Environment.NewLine + Environment.NewLine +
                $"Port: {request.Port}  •  voice_host: {voiceHost}  •  Agent: {agentStatus}" +
                tunnelState;
        }

        private void SetSimpleVoiceChatWarning(InstanceCardViewModel vm, string warning)
        {
            _dispatcher.Invoke(() => vm.SetSimpleVoiceChatWarning(warning));
            SaveSimpleVoiceChatMetadata(vm.Metadata, null, warning, null);
        }

        private void SetSimpleVoiceChatWarning(InstanceCardViewModel vm, string warning, string? status)
        {
            _dispatcher.Invoke(() => vm.SetSimpleVoiceChatWarning(warning));
            SaveSimpleVoiceChatMetadata(vm.Metadata, null, warning, status);
        }

        private void SaveSimpleVoiceChatMetadata(InstanceMetadata metadata, PortCheckRequest? request, string? warning, string? status)
        {
            string? instancePath = _registry.GetPath(metadata.Id);
            if (string.IsNullOrWhiteSpace(instancePath))
            {
                return;
            }

            SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(instancePath);
            metadata.SimpleVoiceChatDetected = detection.IsDetected;
            metadata.SimpleVoiceChatPort = request?.Port ?? detection.Port;
            metadata.SimpleVoiceChatConfigPath = detection.ConfigPath;
            metadata.SimpleVoiceChatVoiceHost = detection.VoiceHost;
            metadata.SimpleVoiceChatLastWarning = warning;
            metadata.SimpleVoiceChatStatus = status ?? metadata.SimpleVoiceChatStatus;
            _instanceManager.SaveMetadata(metadata, instancePath);
        }

        private PortCheckRequest BuildSimpleVoiceChatRequest(
            InstanceMetadata metadata,
            string instancePath,
            SimpleVoiceChatDetection detection)
        {
            return new PortCheckRequest(
                detection.Port,
                PortProtocol.Udp,
                PortIpMode.IPv4,
                bindAddress: string.Equals(detection.BindAddress, "*", StringComparison.OrdinalIgnoreCase) ? null : detection.BindAddress,
                instanceId: metadata.Id,
                instanceName: metadata.Name,
                instancePath: instancePath,
                bindingRole: PortBindingRole.SimpleVoiceChat,
                engine: PortEngine.SimpleVoiceChat);
        }

        private void ApplySimpleVoiceChatTunnel(
            InstanceCardViewModel vm,
            PortCheckRequest request,
            TunnelResolutionResult resolution,
            bool isBeforeStart)
        {
            if (string.IsNullOrWhiteSpace(resolution.PublicAddress) || string.IsNullOrWhiteSpace(request.InstancePath))
            {
                return;
            }

            SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(request.InstancePath);
            string configPath = detection.IsConfigPending || string.IsNullOrWhiteSpace(detection.ConfigPath)
                ? SimpleVoiceChatConfigService.CreateInitialConfig(request.InstancePath, request.Port, resolution.PublicAddress, detection.Source)
                : detection.ConfigPath!;

            bool changed = false;
            if (!detection.IsConfigPending)
            {
                changed = SimpleVoiceChatConfigService.PatchVoiceHost(configPath, resolution.PublicAddress);
            }

            _applicationState.SetVoiceChatTunnelAddress(vm.Id, resolution.PublicAddress);
            if (!string.IsNullOrWhiteSpace(resolution.NumericAddress))
            {
                _applicationState.SetVoiceChatNumericTunnelAddress(vm.Id, resolution.NumericAddress);
            }

            _dispatcher.Invoke(() =>
            {
                vm.VoiceChatTunnelAddress = resolution.PublicAddress;
                vm.VoiceChatNumericTunnelAddress = resolution.NumericAddress;
                vm.ClearSimpleVoiceChatWarning();
                if (!isBeforeStart && changed)
                {
                    vm.SetSimpleVoiceChatWarning("Restart required for Simple Voice Chat tunnel changes to apply.");
                }
            });

            vm.Metadata.SimpleVoiceChatDetected = true;
            vm.Metadata.SimpleVoiceChatPort = request.Port;
            vm.Metadata.SimpleVoiceChatTunnelId = resolution.TunnelId;
            vm.Metadata.SimpleVoiceChatTunnelAddress = resolution.PublicAddress;
            vm.Metadata.SimpleVoiceChatNumericTunnelAddress = resolution.NumericAddress;
            vm.Metadata.SimpleVoiceChatConfigPath = configPath;
            vm.Metadata.SimpleVoiceChatVoiceHost = resolution.PublicAddress;
            vm.Metadata.SimpleVoiceChatLastWarning = !isBeforeStart && changed
                ? "Restart required for Simple Voice Chat tunnel changes to apply."
                : null;
            vm.Metadata.SimpleVoiceChatStatus = "TunnelActive";
            _instanceManager.SaveMetadata(vm.Metadata, request.InstancePath);
        }

        private void ApplySimpleVoiceChatTunnel(
            InstanceMetadata metadata,
            PortCheckRequest request,
            TunnelResolutionResult resolution,
            bool isBeforeStart)
        {
            if (string.IsNullOrWhiteSpace(resolution.PublicAddress) || string.IsNullOrWhiteSpace(request.InstancePath))
            {
                return;
            }

            SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(request.InstancePath);
            string configPath = detection.IsConfigPending || string.IsNullOrWhiteSpace(detection.ConfigPath)
                ? SimpleVoiceChatConfigService.CreateInitialConfig(request.InstancePath, request.Port, resolution.PublicAddress, detection.Source)
                : detection.ConfigPath!;

            bool changed = false;
            if (!detection.IsConfigPending)
            {
                changed = SimpleVoiceChatConfigService.PatchVoiceHost(configPath, resolution.PublicAddress);
            }

            _applicationState.SetVoiceChatTunnelAddress(metadata.Id, resolution.PublicAddress);
            if (!string.IsNullOrWhiteSpace(resolution.NumericAddress))
            {
                _applicationState.SetVoiceChatNumericTunnelAddress(metadata.Id, resolution.NumericAddress);
            }

            metadata.SimpleVoiceChatDetected = true;
            metadata.SimpleVoiceChatPort = request.Port;
            metadata.SimpleVoiceChatTunnelId = resolution.TunnelId;
            metadata.SimpleVoiceChatTunnelAddress = resolution.PublicAddress;
            metadata.SimpleVoiceChatNumericTunnelAddress = resolution.NumericAddress;
            metadata.SimpleVoiceChatConfigPath = configPath;
            metadata.SimpleVoiceChatVoiceHost = resolution.PublicAddress;
            metadata.SimpleVoiceChatLastWarning = !isBeforeStart && changed
                ? "Restart required for Simple Voice Chat tunnel changes to apply."
                : null;
            metadata.SimpleVoiceChatStatus = !isBeforeStart && changed ? "RestartRequired" : "TunnelActive";
            _instanceManager.SaveMetadata(metadata, request.InstancePath);
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
            if (_playitAgentService.State == PlayitAgentState.Connected) return;
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

        private static bool IsSimpleVoiceChatRequest(PortCheckRequest request)
        {
            return request.BindingRole == PortBindingRole.SimpleVoiceChat ||
                   request.Engine == PortEngine.SimpleVoiceChat;
        }

    }
}
