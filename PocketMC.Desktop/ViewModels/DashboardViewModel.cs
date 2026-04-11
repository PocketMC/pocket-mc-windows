using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Views;

namespace PocketMC.Desktop.ViewModels
{
    // The existing InstanceCardViewModel is in Views, but let's assume it's in the namespace.
    // I'll keep the `ObservableCollection<InstanceCardViewModel> Instances` property.
    public class DashboardViewModel : ViewModelBase
    {
        private readonly ApplicationState _applicationState;
        private readonly InstanceManager _instanceManager;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ResourceMonitorService _resourceMonitorService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly TunnelService _tunnelService;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DashboardViewModel> _logger;
        private readonly Dictionary<Guid, InstanceCardViewModel> _instanceLookup = new();
        private readonly HashSet<Guid> _tunnelResolutionsInFlight = new();
        private readonly object _tunnelResolutionLock = new();
        private bool _isActive;

        public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

        public ICommand NewInstanceCommand { get; }
        public ICommand RefreshInstancesCommand { get; }
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand DeleteInstanceCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyCrashReportCommand { get; }
        public ICommand ServerSettingsCommand { get; }
        public ICommand OpenConsoleCommand { get; }

        public DashboardViewModel(
            ApplicationState applicationState,
            InstanceManager instanceManager,
            ServerProcessManager serverProcessManager,
            ResourceMonitorService resourceMonitorService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            TunnelService tunnelService,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider,
            ILogger<DashboardViewModel> logger)
        {
            _applicationState = applicationState;
            _instanceManager = instanceManager;
            _serverProcessManager = serverProcessManager;
            _resourceMonitorService = resourceMonitorService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _tunnelService = tunnelService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;
            _logger = logger;

            NewInstanceCommand = new RelayCommand(_ => NavigateToNewInstance());
            RefreshInstancesCommand = new RelayCommand(_ => LoadInstances());
            StartServerCommand = new RelayCommand(StartServer);
            StopServerCommand = new RelayCommand(StopServer);
            DeleteInstanceCommand = new RelayCommand(DeleteInstance);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            CopyCrashReportCommand = new RelayCommand(CopyCrashReport);
            ServerSettingsCommand = new RelayCommand(OpenSettings);
            OpenConsoleCommand = new RelayCommand(OpenConsole);
        }

        public void Activate()
        {
            if (_isActive)
            {
                LoadInstances();
                return;
            }

            _instanceManager.InstancesChanged += OnInstancesChanged;
            _serverProcessManager.OnInstanceStateChanged += OnInstanceStateChanged;
            _serverProcessManager.OnRestartCountdownTick += OnRestartCountdownTick;
            _resourceMonitorService.OnGlobalMetricsUpdated += OnGlobalMetricsUpdated;
            _isActive = true;
            LoadInstances();
        }

        public void Deactivate()
        {
            if (!_isActive)
            {
                return;
            }

            _instanceManager.InstancesChanged -= OnInstancesChanged;
            _serverProcessManager.OnInstanceStateChanged -= OnInstanceStateChanged;
            _serverProcessManager.OnRestartCountdownTick -= OnRestartCountdownTick;
            _resourceMonitorService.OnGlobalMetricsUpdated -= OnGlobalMetricsUpdated;
            _isActive = false;
        }

        private void OnInstancesChanged(object? sender, EventArgs e)
        {
            _dispatcher.Invoke(LoadInstances);
        }

        private void OnInstanceStateChanged(Guid instanceId, ServerState state)
        {
            _dispatcher.Invoke(() =>
            {
                if (!_instanceLookup.TryGetValue(instanceId, out var vm))
                {
                    return;
                }

                vm.UpdateState(state);
                ApplyLiveMetrics(vm);
            });
        }

        private void OnRestartCountdownTick(Guid instanceId, int secondsRemaining)
        {
            _dispatcher.Invoke(() =>
            {
                if (_instanceLookup.TryGetValue(instanceId, out var vm))
                {
                    vm.UpdateCountdown(secondsRemaining);
                    ApplyLiveMetrics(vm);
                }
            });
        }

        private void OnGlobalMetricsUpdated()
        {
            _dispatcher.Invoke(UpdateAllLiveMetrics);
        }

        private void NavigateToNewInstance()
        {
            var newInstancePage = ActivatorUtilities.CreateInstance<NewInstancePage>(_serviceProvider);
            _navigationService.NavigateToDetailPage(
                newInstancePage,
                "New Instance",
                DetailRouteKind.NewInstance,
                DetailBackNavigation.Dashboard,
                clearDetailStack: true);
        }

        public void LoadInstances()
        {
            if (!_applicationState.IsConfigured) return;

            var existingVms = Instances.ToList();
            Instances.Clear();
            _instanceLookup.Clear();
            var metas = _instanceManager.GetAllInstances();
            foreach (var meta in metas)
            {
                var existing = existingVms.FirstOrDefault(v => v.Id == meta.Id);
                if (existing != null)
                {
                    existing.UpdateFromMetadata(meta);
                    Instances.Add(existing);
                }
                else
                {
                    var newVm = new InstanceCardViewModel(meta, _serverProcessManager);
                    Instances.Add(newVm);
                }
            }

            foreach (var vm in Instances)
            {
                _instanceLookup[vm.Id] = vm;
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process != null)
                {
                    vm.UpdateState(process.State);
                }

                // Populate MaxPlayers from server.properties
                if (TryGetServerProperty(vm.Id, "max-players", out string? maxPlayerStr) &&
                    int.TryParse(maxPlayerStr, out int maxPlayers) && maxPlayers > 0)
                {
                    vm.MaxPlayers = maxPlayers;
                }

                ApplyLiveMetrics(vm);

                // Pre-populate tunnel address from cache (no polling)
                var cached = _applicationState.GetTunnelAddress(vm.Id);
                if (!string.IsNullOrEmpty(cached))
                {
                    vm.TunnelAddress = cached;
                }
            }
        }

        private void UpdateAllLiveMetrics()
        {
            foreach (var vm in Instances)
            {
                ApplyLiveMetrics(vm);
            }
        }

        private void ApplyLiveMetrics(InstanceCardViewModel vm)
        {
            if (_resourceMonitorService.Metrics.TryGetValue(vm.Id, out var metrics))
            {
                double maxRamGb = vm.Metadata.MaxRamMb / 1024.0;
                double usedRamGb = metrics.RamUsageMb / 1024.0;

                vm.CpuText = $"{Math.Round(metrics.CpuUsage):0}%";
                vm.RamText = $"{usedRamGb:F1} / {maxRamGb:F0} GB";
                vm.PlayerStatus = $"{metrics.PlayerCount} / {vm.MaxPlayers}";
                return;
            }

            // No metrics — show placeholder only if server isn't running
            if (!vm.IsRunning)
            {
                vm.CpuText = "\u00b7 \u00b7 \u00b7";
                vm.RamText = "\u00b7 \u00b7 \u00b7";
                vm.PlayerStatus = "\u00b7 \u00b7 \u00b7";
            }
        }



        private async void StartServer(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                try
                {
                    string? instancePath = _instanceManager.GetInstancePath(vm.Id);
                    if (instancePath == null) return;

                    var process = await _serverProcessManager.StartProcessAsync(vm.Metadata, _applicationState.GetRequiredAppRootPath());
                    vm.UpdateState(process.State);
                    ApplyLiveMetrics(vm);

                    _ = EnsureTunnelFlowForInstanceAsync(vm);
                }
                catch (Exception ex)
                {
                    vm.UpdateState(ServerState.Stopped);
                    ApplyLiveMetrics(vm);
                    _logger.LogError(ex, "Failed to start server {ServerName}.", vm.Name);
                    _dialogService.ShowMessage("Start Failed", $"PocketMC could not start '{vm.Name}'.\n\n{ex.Message}", DialogType.Error);
                }
            }
        }

        private async Task EnsureTunnelFlowForInstanceAsync(InstanceCardViewModel vm)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()))
            {
                return;
            }

            if (!TryBeginTunnelResolution(vm.Id))
            {
                return;
            }

            try
            {
                if (!TryGetServerPort(vm.Id, out int serverPort))
                {
                    _logger.LogDebug("Skipping tunnel resolution for {ServerName} because the server port could not be read.", vm.Name);
                    return;
                }

                _dispatcher.Invoke(() => vm.TunnelAddress = null);
                EnsurePlayitAgentRunning();

                TunnelResolutionResult resolution = await ResolveTunnelWithWarmupAsync(serverPort);
                switch (resolution.Status)
                {
                    case TunnelResolutionResult.TunnelStatus.Found:
                        if (!string.IsNullOrWhiteSpace(resolution.PublicAddress))
                        {
                            _applicationState.SetTunnelAddress(vm.Id, resolution.PublicAddress!);
                            _dispatcher.Invoke(() => vm.TunnelAddress = resolution.PublicAddress);
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
                                    _applicationState.SetTunnelAddress(vm.Id, address);
                                }
                                _dispatcher.Invoke(() => vm.TunnelAddress = address);
                            };
                            _navigationService.NavigateToDetailPage(
                                guidePage,
                                $"Tunnel Setup: {vm.Name}",
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
                        _logger.LogInformation("Playit agent is not ready yet for server {ServerName}.", vm.Name);
                        break;

                    case TunnelResolutionResult.TunnelStatus.Error:
                        if (resolution.RequiresClaim)
                        {
                            _logger.LogInformation("Playit claim is still pending for server {ServerName}.", vm.Name);
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
                            _logger.LogWarning("Playit tunnel resolution failed for {ServerName}: {Message}", vm.Name, resolution.ErrorMessage);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to complete the Playit tunnel flow for {ServerName}.", vm.Name);
            }
            finally
            {
                EndTunnelResolution(vm.Id);
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

        private void EnsurePlayitAgentRunning()
        {
            if (_playitAgentService.IsRunning)
            {
                return;
            }

            if (_playitAgentService.State is PlayitAgentState.WaitingForClaim or PlayitAgentState.Starting)
            {
                return;
            }

            _playitAgentService.Start();
        }

        private bool TryGetServerPort(Guid instanceId, out int serverPort)
        {
            serverPort = 0;
            string? instancePath = _instanceManager.GetInstancePath(instanceId);
            if (string.IsNullOrWhiteSpace(instancePath))
            {
                return false;
            }

            string propsFile = Path.Combine(instancePath, "server.properties");
            if (!File.Exists(propsFile))
            {
                return false;
            }

            var props = ServerPropertiesParser.Read(propsFile);
            return props.TryGetValue("server-port", out string? portString) && int.TryParse(portString, out serverPort);
        }

        private bool TryGetServerProperty(Guid instanceId, string key, out string? value)
        {
            value = null;
            string? instancePath = _instanceManager.GetInstancePath(instanceId);
            if (string.IsNullOrWhiteSpace(instancePath)) return false;

            string propsFile = Path.Combine(instancePath, "server.properties");
            if (!File.Exists(propsFile)) return false;

            var props = ServerPropertiesParser.Read(propsFile);
            return props.TryGetValue(key, out value);
        }

        private bool TryBeginTunnelResolution(Guid instanceId)
        {
            lock (_tunnelResolutionLock)
            {
                return _tunnelResolutionsInFlight.Add(instanceId);
            }
        }

        private void EndTunnelResolution(Guid instanceId)
        {
            lock (_tunnelResolutionLock)
            {
                _tunnelResolutionsInFlight.Remove(instanceId);
            }
        }



        private async void StopServer(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                try
                {
                    if (_serverProcessManager.IsWaitingToRestart(vm.Id))
                    {
                        _serverProcessManager.AbortRestartDelay(vm.Id);
                        vm.UpdateState(ServerState.Crashed);
                        ApplyLiveMetrics(vm);
                        return;
                    }

                    if (_serverProcessManager.GetProcess(vm.Id) == null)
                    {
                        return;
                    }

                    vm.UpdateState(ServerState.Stopping);
                    await _serverProcessManager.StopProcessAsync(vm.Id);
                }
                catch (Exception ex)
                {
                    var currentState = _serverProcessManager.GetProcess(vm.Id)?.State ?? ServerState.Stopped;
                    vm.UpdateState(currentState);
                    ApplyLiveMetrics(vm);
                    _logger.LogError(ex, "Failed to stop server {ServerName}.", vm.Name);
                    _dialogService.ShowMessage("Stop Failed", $"PocketMC could not stop '{vm.Name}' cleanly.\n\n{ex.Message}", DialogType.Error);
                }
                finally
                {
                    // Clear cached tunnel address on stop
                    _applicationState.ClearTunnelAddress(vm.Id);
                    vm.TunnelAddress = null;
                }
            }
        }

        private async void DeleteInstance(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                if (_serverProcessManager.IsRunning(vm.Id))
                {
                    _dialogService.ShowMessage("Server Running", "Cannot delete a running server. Stop it first.", DialogType.Warning);
                    return;
                }

                var prompt = await _dialogService.ShowDialogAsync("Delete Server", $"Are you sure you want to completely erase the {vm.Name} server? All worlds and files will be permanently deleted.", DialogType.Warning, false);
                if (prompt == DialogResult.Yes)
                {
                    string? path = _instanceManager.GetInstancePath(vm.Id);
                    if (path != null && Directory.Exists(path))
                    {
                        await PocketMC.Desktop.Utils.FileUtils.CleanDirectoryAsync(path);
                        Directory.Delete(path, true);
                    }
                    LoadInstances();
                }
            }
        }

        private void OpenFolder(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                string? path = _instanceManager.GetInstancePath(vm.Id);
                if (path != null && Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
        }

        private void CopyCrashReport(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                string? path = _instanceManager.GetInstancePath(vm.Id);
                if (path == null) return;

                var crashReportsDir = Path.Combine(path, "crash-reports");
                if (Directory.Exists(crashReportsDir))
                {
                    var latestReport = new DirectoryInfo(crashReportsDir)
                        .GetFiles("*.txt")
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (latestReport != null)
                    {
                        string content = File.ReadAllText(latestReport.FullName);
                        System.Windows.Clipboard.SetText(content);
                        _dialogService.ShowMessage("Copied", "The latest crash report has been copied to your clipboard.", DialogType.Information);
                        return;
                    }
                }

                _dialogService.ShowMessage("No Crash Reports", "No crash reports found for this server.", DialogType.Information);
            }
        }

        private void OpenSettings(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                var settingsViewModel = ActivatorUtilities.CreateInstance<ServerSettingsViewModel>(_serviceProvider, vm.Metadata);
                var settingsPage = ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, settingsViewModel);
                _navigationService.NavigateToDetailPage(
                    settingsPage,
                    $"Settings: {vm.Name}",
                    DetailRouteKind.ServerSettings,
                    DetailBackNavigation.Dashboard,
                    clearDetailStack: true);
            }
        }

        private void OpenConsole(object? parameter)
        {
            if (parameter is InstanceCardViewModel vm)
            {
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process == null)
                {
                    _dialogService.ShowMessage("Unavailable", "Start the server at least once before opening the console.", DialogType.Information);
                    return;
                }

                var consolePage = ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, process);
                _navigationService.NavigateToDetailPage(
                    consolePage,
                    $"Console: {vm.Name}",
                    DetailRouteKind.ServerConsole,
                    DetailBackNavigation.Dashboard,
                    clearDetailStack: true);
            }
        }
    }
}
