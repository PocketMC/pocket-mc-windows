using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Intelligence;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardActionsVM
    {
        private readonly ApplicationState _applicationState;
        private readonly InstanceManager _instanceManager;
        private readonly InstanceRegistry _registry;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly ServerProcessManager _serverProcessManager; // Still needed for port checking or active processes
        private readonly ServerConfigurationService _serverConfigurationService;
        private readonly InstanceTunnelOrchestrator _tunnelOrchestrator;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DashboardActionsVM> _logger;

        public DashboardActionsVM(
            ApplicationState applicationState,
            InstanceManager instanceManager,
            InstanceRegistry registry,
            IServerLifecycleService lifecycleService,
            ServerProcessManager serverProcessManager,
            ServerConfigurationService serverConfigurationService,
            InstanceTunnelOrchestrator tunnelOrchestrator,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            ILogger<DashboardActionsVM> logger)
        {
            _applicationState = applicationState;
            _instanceManager = instanceManager;
            _registry = registry;
            _lifecycleService = lifecycleService;
            _serverProcessManager = serverProcessManager;
            _serverConfigurationService = serverConfigurationService;
            _tunnelOrchestrator = tunnelOrchestrator;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async void StartServer(InstanceCardViewModel vm, Action<InstanceCardViewModel> onStarted)
        {
            try
            {
                string? instancePath = _registry.GetPath(vm.Id);
                if (instancePath == null) return;

                var availableMb = MemoryHelper.GetAvailablePhysicalMemoryMb();
                var requiredMb = (ulong)vm.Metadata.MaxRamMb;
                if (availableMb < requiredMb + 512)
                {
                    var result = await _dialogService.ShowDialogAsync("Low Memory",
                        $"Your system only has {availableMb}MB of available RAM. Starting this server ({requiredMb}MB) might cause significant lag or crashes.\n\nContinue anyway?",
                        DialogType.Warning, true);
                    if (result != DialogResult.Yes) return;
                }

                int targetPort = _serverConfigurationService.GetActivePortForInstance(vm.Id);
                var otherRunningPorts = _serverProcessManager.ActiveProcesses
                    .Where(kvp => kvp.Key != vm.Id)
                    .Select(kvp => _registry.GetPath(kvp.Key))
                    .Where(p => p != null)
                    .Select(p =>
                    {
                        _serverConfigurationService.TryGetProperty(p!, "server-port", out string? portStr);
                        return int.TryParse(portStr, out int port) ? port : 25565;
                    });

                if (otherRunningPorts.Contains(targetPort))
                {
                    await _dialogService.ShowDialogAsync("Port Collision",
                        $"Another running server is already using port {targetPort}.",
                        DialogType.Warning);
                    return;
                }

                await _lifecycleService.StartAsync(vm.Metadata);
                // The update state will be handled by listening to OnInstanceStateChanged in DashboardViewModel or CardVM.
                onStarted(vm);

                _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm.Id, vm.Name, addr => vm.TunnelAddress = addr);
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
                        $"Generate an AI summary for this '{vm.Name}' session?\n\nThis will send the session logs to {settings.AiProvider} for analysis.",
                        DialogType.Question, true);

                    if (response != DialogResult.Yes)
                        return;
                }

                // Run summarization asynchronously
                var summarizationService = (SessionSummarizationService)_serviceProvider.GetService(typeof(SessionSummarizationService))!;
                var provider = AiApiClient.ParseProvider(settings.AiProvider);
                var sessionEnd = DateTime.UtcNow;

                var notificationService = (INotificationService)_serviceProvider.GetService(typeof(INotificationService))!;
                notificationService.ShowInformation("AI Summary", $"Generating summary for '{vm.Name}'...");

                var result = await summarizationService.SummarizeAsync(
                    serverDir, vm.Name, provider, settings.GetCurrentAiKey()!, sessionStart, sessionEnd);

                if (result.Success)
                {
                    notificationService.ShowInformation("AI Summary Complete", $"Session summary saved for '{vm.Name}'.");
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
                vm.UpdateState(ServerState.Stopping);
                await _lifecycleService.RestartAsync(vm.Id);
                onStarted(vm);
                _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm.Id, vm.Name, addr => vm.TunnelAddress = addr);
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

        public void CopyCrashReport(InstanceCardViewModel vm)
        {
            string? path = _registry.GetPath(vm.Id);
            if (path == null) return;
            var crashReportsDir = Path.Combine(path, "crash-reports");
            if (Directory.Exists(crashReportsDir))
            {
                var latest = new DirectoryInfo(crashReportsDir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (latest != null)
                {
                    System.Windows.Clipboard.SetText(File.ReadAllText(latest.FullName));
                    _dialogService.ShowMessage("Copied", "Crash report copied to clipboard.", DialogType.Information);
                    return;
                }
            }
            _dialogService.ShowMessage("Not Found", "No crash reports found.", DialogType.Information);
        }

        public void OpenSettings(InstanceCardViewModel vm)
        {
            var settingsViewModel = ActivatorUtilities.CreateInstance<ServerSettingsViewModel>(_serviceProvider, vm.Metadata);
            var settingsPage = ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, settingsViewModel);
            _navigationService.NavigateToDetailPage(settingsPage, $"Settings: {vm.Name}", DetailRouteKind.ServerSettings, DetailBackNavigation.Dashboard, true);
        }

        public void OpenConsole(InstanceCardViewModel vm)
        {
            var process = _lifecycleService.GetProcess(vm.Id);
            if (process == null) return;
            var consolePage = ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, process);
            _navigationService.NavigateToDetailPage(consolePage, $"Console: {vm.Name}", DetailRouteKind.ServerConsole, DetailBackNavigation.Dashboard, true);
        }
    }
}
