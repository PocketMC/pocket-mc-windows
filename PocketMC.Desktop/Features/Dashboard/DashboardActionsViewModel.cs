using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Application.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Players;
using PocketMC.Desktop.Features.Instances.ImportExport;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardActionsViewModel
    {
        private readonly ApplicationState _applicationState;
        private readonly InstanceManager _instanceManager;
        private readonly InstanceRegistry _registry;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly InstanceTunnelOrchestrator _tunnelOrchestrator;
        private readonly PortFailureMessageService _portFailureMessageService;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly AgentProvisioningService _agentProvisioning;
        private readonly ILogger<DashboardActionsViewModel> _logger;
        private readonly ServerConfigurationService _configService;
        private readonly PortProbeService _probeService;

        public DashboardActionsViewModel(
            ApplicationState applicationState,
            InstanceManager instanceManager,
            InstanceRegistry registry,
            IServerLifecycleService lifecycleService,
            InstanceTunnelOrchestrator tunnelOrchestrator,
            PortFailureMessageService portFailureMessageService,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            AgentProvisioningService agentProvisioning,
            ILogger<DashboardActionsViewModel> logger,
            ServerConfigurationService configService,
            PortProbeService probeService)
        {
            _applicationState = applicationState;
            _instanceManager = instanceManager;
            _registry = registry;
            _lifecycleService = lifecycleService;
            _tunnelOrchestrator = tunnelOrchestrator;
            _portFailureMessageService = portFailureMessageService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _agentProvisioning = agentProvisioning;
            _logger = logger;
            _configService = configService;
            _probeService = probeService;
        }

        public async void StartServer(InstanceCardViewModel vm, Action<InstanceCardViewModel> onStarted)
        {
            try
            {
                if (vm.IsRunning) return;

                vm.UpdateState(ServerState.SettingUp);
                onStarted(vm);

                var agentState = await _agentProvisioning.GetConnectionStateAsync();
                if (agentState != AgentConnectionState.Connected && agentState != AgentConnectionState.Connecting)
                {
                    var dialog = new PreStartAgentWarningWindow(_agentProvisioning)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    dialog.Show();
                    var dialogResult = await dialog.WaitForResultAsync();
                    if (!dialogResult)
                    {
                        vm.UpdateState(ServerState.Stopped);
                        onStarted(vm);
                        return;
                    }
                }

                var availableMb = MemoryHelper.GetAvailablePhysicalMemoryMb();
                var requiredMb = (ulong)vm.Metadata.MaxRamMb;
                if (availableMb < requiredMb + 512)
                {
                    var result = await _dialogService.ShowDialogAsync("Low Memory",
                        $"Your system only has {availableMb}MB of available RAM. Starting this server ({requiredMb}MB) might cause significant lag or crashes.\n\nContinue anyway?",
                        DialogType.Warning, true);
                    if (result != DialogResult.Yes)
                    {
                        vm.UpdateState(ServerState.Stopped);
                        onStarted(vm);
                        return;
                    }
                }

                if (!await _tunnelOrchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm, isBeforeLaunch: true))
                {
                    vm.UpdateState(ServerState.Stopped);
                    onStarted(vm);
                    return;
                }

                if (vm.Metadata.AutoUpdateAddons)
                {
                    try
                    {
                        var autoUpdateService = _serviceProvider.GetRequiredService<AddonAutoUpdateService>();
                        var instancePath = _registry.GetPath(vm.Id);
                        if (instancePath != null)
                        {
                            vm.StateTextOverride = "⚙ Checking mods/plugins Updates...";
                            await autoUpdateService.CheckAndPromptUpdatesAsync(vm.Metadata, instancePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to run addon auto-updater for {ServerName}.", vm.Name);
                    }
                    finally
                    {
                        vm.StateTextOverride = null;
                    }
                }

                await _lifecycleService.StartAsync(vm.Metadata);
                vm.ClearPortIssue();
                // The update state will be handled by listening to OnInstanceStateChanged in DashboardViewModel or CardVM.
                onStarted(vm);

                _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm);
            }
            catch (PortReliabilityException ex)
            {
                await HandlePortReliabilityFailureAsync(vm, ex, "startup", onStarted);
            }
            catch (Exception ex)
            {
                vm.UpdateState(ServerState.Stopped);
                onStarted(vm);
                _logger.LogError(ex, "Failed to start server {ServerName}.", vm.Name);
                _dialogService.ShowMessage("Start Failed", $"PocketMC could not start '{vm.Name}'.\n\n{ex.Message}", DialogType.Error);
            }
        }

        public async void StopServer(InstanceCardViewModel vm, Action<InstanceCardViewModel> onStopped)
        {
            try
            {
                if (_lifecycleService.IsWaitingToRestart(vm.Id))
                {
                    _lifecycleService.AbortRestartDelay(vm.Id);
                    vm.UpdateState(ServerState.Crashed);
                    onStopped(vm);
                    return;
                }

                if (_lifecycleService.GetProcess(vm.Id) == null) return;

                // Capture session start time before stopping
                var sessionStart = _lifecycleService.GetSessionStartTime(vm.Id) ?? DateTime.UtcNow;

                vm.UpdateState(ServerState.Stopping);
                await _lifecycleService.StopAsync(vm.Id);

                // Trigger AI summarization flow after stop completes
                _ = TriggerAiSummarizationAsync(vm, sessionStart);
            }
            catch (Exception ex)
            {
                var currentState = _lifecycleService.GetProcess(vm.Id)?.State ?? ServerState.Stopped;
                vm.UpdateState(currentState);
                onStopped(vm);
                _logger.LogError(ex, "Failed to stop server {ServerName}.", vm.Name);
            }
        }

        private async Task TriggerAiSummarizationAsync(InstanceCardViewModel vm, DateTime sessionStart)
        {
            try
            {
                var settings = _applicationState.Settings;

                // Check if feature is enabled and configured
                if (!settings.EnableAiSummarization || string.IsNullOrWhiteSpace(settings.GetCurrentAiKey()))
                    return;

                string? serverDir = _registry.GetPath(vm.Id);
                if (serverDir == null) return;

                // Either auto-generate or ask the user
                if (!settings.AlwaysAutoSummarize)
                {
                    var response = await _dialogService.ShowDialogAsync(
                        "AI Session Summary",
                        $"Generate an AI summary for this '{vm.Name}' session?\n\nThis will send the session logs to {settings.AiProvider ?? "Gemini"} for analysis.",
                        DialogType.Question, true);

                    if (response != DialogResult.Yes)
                        return;
                }

                // Run summarization asynchronously
                var summarizationService = (SessionSummarizationService)_serviceProvider.GetService(typeof(SessionSummarizationService))!;
                var provider = ((ILlmProviderFactory)_serviceProvider.GetService(typeof(ILlmProviderFactory))!).ParseProvider(settings.AiProvider ?? "Gemini");
                var sessionEnd = DateTime.UtcNow;

                var notificationService = (INotificationService)_serviceProvider.GetService(typeof(INotificationService))!;
                if (settings.EnableAiSummaryNotifications)
                {
                    notificationService.ShowInformation("AI Summary", $"Generating summary for '{vm.Name}'...");
                }

                var result = await summarizationService.SummarizeAsync(
                    serverDir, vm.Name, provider, settings.GetCurrentAiKey()!, settings.GetCurrentAiModel(), settings.GetCurrentAiEndpoint(), sessionStart, sessionEnd);

                if (result.Success)
                {
                    if (settings.EnableAiSummaryNotifications)
                    {
                        notificationService.ShowSummaryComplete(vm.Id.ToString(), vm.Name);
                    }
                }
                else
                {
                    _dialogService.ShowMessage("AI Summary Failed", $"Could not generate summary:\n{result.Error}", DialogType.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI summarization failed for {Server}.", vm.Name);
                // Intentionally swallowed — summarization failure must never affect server operations
            }
        }

        public async void RestartServer(InstanceCardViewModel vm, Action<InstanceCardViewModel> onStarted)
        {
            try
            {
                var previousState = vm.StatusText.Contains("Online") ? ServerState.Online : ServerState.Starting;
                vm.UpdateState(ServerState.SettingUp);
                onStarted(vm);

                var agentState = await _agentProvisioning.GetConnectionStateAsync();
                if (agentState != AgentConnectionState.Connected && agentState != AgentConnectionState.Connecting)
                {
                    var dialog = new PreStartAgentWarningWindow(_agentProvisioning)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    dialog.Show();
                    var dialogResult = await dialog.WaitForResultAsync();
                    if (!dialogResult)
                    {
                        vm.UpdateState(previousState);
                        onStarted(vm);
                        return;
                    }
                }

                if (!await _tunnelOrchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm, isBeforeLaunch: true))
                {
                    vm.UpdateState(previousState);
                    onStarted(vm);
                    return;
                }

                await _lifecycleService.RestartAsync(vm.Id);
                vm.ClearPortIssue();
                onStarted(vm);
                _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm);
            }
            catch (PortReliabilityException ex)
            {
                await HandlePortReliabilityFailureAsync(vm, ex, "restart", onStarted);
            }
            catch (Exception ex)
            {
                vm.UpdateState(ServerState.Stopped);
                onStarted(vm);
                _logger.LogError(ex, "Failed to restart server {ServerName}.", vm.Name);
                _dialogService.ShowMessage("Restart Failed", $"PocketMC could not restart '{vm.Name}'.\n\n{ex.Message}", DialogType.Error);
            }
        }

        public async Task DeleteInstanceAsync(InstanceCardViewModel vm)
        {
            if (_lifecycleService.IsRunning(vm.Id))
            {
                _dialogService.ShowMessage("Server Running", "Cannot delete a running server. Stop it first.", DialogType.Warning);
                return;
            }

            var prompt = await _dialogService.ShowDialogAsync("Delete Server", $"Are you sure you want to completely erase the {vm.Name} server?", DialogType.Warning, false);
            if (prompt == DialogResult.Yes)
            {
                await _lifecycleService.ReleaseInstanceAsync(vm.Id);
                await _instanceManager.DeleteInstanceAsync(vm.Id);
            }
        }

        public void OpenFolder(InstanceCardViewModel vm)
        {
            string? path = _registry.GetPath(vm.Id);
            if (path != null && Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }

        public async void CopyCrashReport(InstanceCardViewModel vm)
        {
            string? path = _registry.GetPath(vm.Id);
            if (path == null) return;
            var crashReportsDir = Path.Combine(path, "crash-reports");
            if (Directory.Exists(crashReportsDir))
            {
                var latest = new DirectoryInfo(crashReportsDir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (latest != null)
                {
                    bool ok = await Infrastructure.ClipboardHelper.TrySetTextAsync(File.ReadAllText(latest.FullName));
                    if (ok)
                        _dialogService.ShowMessage("Copied", "Crash report copied to clipboard.", DialogType.Information);
                    else
                        _dialogService.ShowMessage("Clipboard Error", "Failed to copy crash report. The clipboard may be locked by another application.", DialogType.Warning);
                    return;
                }
            }
            _dialogService.ShowMessage("Not Found", "No crash reports found.", DialogType.Information);
        }

        public void OpenExportPage(InstanceCardViewModel vm)
        {
            try
            {
                string? instancePath = _registry.GetPath(vm.Id);
                if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
                {
                    _dialogService.ShowMessage("Export Unavailable", "PocketMC could not find this instance folder.", DialogType.Warning);
                    return;
                }

                var viewModel = ActivatorUtilities.CreateInstance<InstanceExportViewModel>(
                    _serviceProvider,
                    vm.Metadata,
                    instancePath);

                if (vm.IsRunning)
                {
                    viewModel.UseConfigOnlyDefaultForRunningServer();
                }

                var page = ActivatorUtilities.CreateInstance<InstanceExportPage>(
                    _serviceProvider,
                    viewModel);
                _navigationService.NavigateToDetailPage(
                    page,
                    $"Export: {vm.Name}",
                    DetailRouteKind.InstanceExport,
                    DetailBackNavigation.Dashboard,
                    clearDetailStack: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open the export page for {ServerName}.", vm.Name);
                _dialogService.ShowMessage(
                    "Export Failed",
                    $"PocketMC could not open the export page for '{vm.Name}'.\n\n{ex.Message}",
                    DialogType.Error);
            }
        }

        public void OpenSettings(InstanceCardViewModel vm)
        {
            var settingsViewModel = ActivatorUtilities.CreateInstance<ServerSettingsViewModel>(_serviceProvider, vm.Metadata);
            var settingsPage = ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, settingsViewModel);
            _navigationService.NavigateToDetailPage(settingsPage, $"Settings: {vm.Name}", DetailRouteKind.ServerSettings, DetailBackNavigation.Dashboard, true);
        }

        public void OpenConsole(InstanceCardViewModel vm)
        {
            string? instancePath = _registry.GetPath(vm.Id);
            if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath)) return;

            var process = _lifecycleService.GetProcess(vm.Id);
            var consolePage = process != null
                ? ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, process, instancePath)
                : ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, instancePath);
            _navigationService.NavigateToDetailPage(consolePage, $"Console: {vm.Name}", DetailRouteKind.ServerConsole, DetailBackNavigation.Dashboard, true);
        }

        public void OpenPlayers(InstanceCardViewModel vm)
        {
            var process = _lifecycleService.GetProcess(vm.Id);

            var viewModel = process != null
                ? ActivatorUtilities.CreateInstance<PlayerManagementViewModel>(_serviceProvider, vm.Metadata, process)
                : ActivatorUtilities.CreateInstance<PlayerManagementViewModel>(_serviceProvider, vm.Metadata);

            var page = ActivatorUtilities.CreateInstance<PlayerManagementPage>(_serviceProvider, viewModel);
            _navigationService.NavigateToDetailPage(page, $"Players: {vm.Name}", DetailRouteKind.PlayerManagement, DetailBackNavigation.Dashboard, true);
        }

        public void TogglePin(InstanceCardViewModel vm)
        {
            if (vm.Metadata.PinnedAt.HasValue)
                vm.Metadata.PinnedAt = null;
            else
                vm.Metadata.PinnedAt = DateTime.UtcNow;
                
            string? path = _registry.GetPath(vm.Id);
            if (path != null)
            {
                _instanceManager.SaveMetadata(vm.Metadata, path);
            }
        }

        private async Task HandlePortReliabilityFailureAsync(
            InstanceCardViewModel vm,
            PortReliabilityException ex,
            string operation,
            Action<InstanceCardViewModel> onStateChanged)
        {
            PortFailureDisplayInfo displayInfo = _portFailureMessageService.CreateDisplayInfo(ex, vm.Name);
            PortCheckResult primary = ex.PrimaryResult;

            vm.UpdateState(ServerState.Stopped);
            vm.SetPortIssue(displayInfo.BadgeText, displayInfo.Message);
            onStateChanged(vm);

            _logger.LogWarning(
                "Port reliability blocked {Operation} for server {ServerName}. Code={FailureCode}, Binding={Binding}, Engine={Engine}, Port={Port}, Protocol={Protocol}, IpMode={IpMode}",
                operation,
                vm.Name,
                primary.FailureCode,
                primary.Request.BindingRole,
                primary.Request.Engine,
                primary.Request.Port,
                primary.Request.Protocol,
                primary.Request.IpMode);

            bool isPortChangeable = primary.FailureCode is
                PortFailureCode.InUseByPocketMcInstance or
                PortFailureCode.InUseByExternalProcess or
                PortFailureCode.TcpConflict or
                PortFailureCode.UdpConflict or
                PortFailureCode.AccessDenied or
                PortFailureCode.IPv4BindFailure or
                PortFailureCode.IPv6BindFailure;

            if (isPortChangeable)
            {
                var dialog = new PortConflictWindow(displayInfo.Title, displayInfo.Message, primary.Request.Port, _probeService)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                dialog.ShowDialog();

                if (dialog.UserConfirmed && dialog.NewPort.HasValue)
                {
                    int newPort = dialog.NewPort.Value;
                    string? serverDir = _registry.GetPath(vm.Id);

                    if (serverDir != null)
                    {
                        try
                        {
                            // Update state immediately so user isn't in a blind state while tunnel resolves
                            vm.UpdateState(ServerState.SettingUp);
                            vm.ClearPortIssue();
                            onStateChanged(vm);

                            // Update configuration (which may take time if creating a tunnel)
                            var portUpdater = _serviceProvider.GetRequiredService<PocketMC.Desktop.Features.Networking.InstancePortUpdateService>();
                            await portUpdater.UpdatePortAsync(vm.Metadata, serverDir, primary.Request.BindingRole, newPort);

                            if (operation == "restart")
                            {
                                await _lifecycleService.RestartAsync(vm.Id);
                            }
                            else
                            {
                                await _lifecycleService.StartAsync(vm.Metadata);
                            }

                            onStateChanged(vm);
                            _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm);
                        }
                        catch (PortReliabilityException retryEx)
                        {
                            await HandlePortReliabilityFailureAsync(vm, retryEx, operation, onStateChanged);
                        }
                        catch (Exception retryEx)
                        {
                            vm.UpdateState(ServerState.Stopped);
                            onStateChanged(vm);
                            _logger.LogError(retryEx, "Failed to start server {ServerName} after port change.", vm.Name);
                            _dialogService.ShowMessage("Start Failed", $"PocketMC could not start '{vm.Name}'.\n\n{retryEx.Message}", DialogType.Error);
                        }
                    }
                }
            }
            else
            {
                _dialogService.ShowMessage(displayInfo.Title, displayInfo.Message, DialogType.Warning);
            }
        }
    }
}





