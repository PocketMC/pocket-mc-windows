using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Tunnel;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly DashboardInstanceListVM _listVm;
        private readonly DashboardMetricsVM _metricsVm;
        private readonly DashboardActionsVM _actionsVm;
        private readonly InstanceTunnelOrchestrator _tunnelOrchestrator;

        private readonly InstanceRegistry _registry;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly IResourceMonitorService _resourceMonitorService;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationState _applicationState;
        private readonly PlayitApiClient _playitApiClient;

        private bool _isActive;

        public ObservableCollection<InstanceCardViewModel> Instances => _listVm.Instances;
        public ICommand NewInstanceCommand { get; }
        public ICommand ImportInstanceCommand { get; }
        public ICommand RefreshInstancesCommand { get; }
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand RestartServerCommand { get; }
        public ICommand DeleteInstanceCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyCrashReportCommand { get; }
        public ICommand ExportInstanceCommand { get; }
        public ICommand ServerSettingsCommand { get; }
        public ICommand OpenConsoleCommand { get; }
        public ICommand OpenPlayersCommand { get; }
        public DashboardViewModel(
            DashboardInstanceListVM listVm,
            DashboardMetricsVM metricsVm,
            DashboardActionsVM actionsVm,
            InstanceTunnelOrchestrator tunnelOrchestrator,
            InstanceRegistry registry,
            IServerLifecycleService lifecycleService,
            IResourceMonitorService resourceMonitorService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider,
            ApplicationState applicationState,
            PlayitApiClient playitApiClient)
        {
            _listVm = listVm;
            _metricsVm = metricsVm;
            _actionsVm = actionsVm;
            _tunnelOrchestrator = tunnelOrchestrator;
            _registry = registry;
            _lifecycleService = lifecycleService;
            _resourceMonitorService = resourceMonitorService;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;
            _applicationState = applicationState;
            _playitApiClient = playitApiClient;

            NewInstanceCommand = new RelayCommand(_ => NavigateToNewInstance());
            ImportInstanceCommand = new RelayCommand(_ => NavigateToImportInstance());
            RefreshInstancesCommand = new RelayCommand(_ => _listVm.LoadInstances());
            StartServerCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.StartServer(vm, _metricsVm.ApplyLiveMetrics); });
            StopServerCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.StopServer(vm, _metricsVm.ApplyLiveMetrics); });
            RestartServerCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.RestartServer(vm, _metricsVm.ApplyLiveMetrics); });
            DeleteInstanceCommand = new AsyncRelayCommand(async p => { if (p is InstanceCardViewModel vm) await _actionsVm.DeleteInstanceAsync(vm); });
            OpenFolderCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenFolder(vm); });
            CopyCrashReportCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.CopyCrashReport(vm); });
            ExportInstanceCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenExportPage(vm); });
            ServerSettingsCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenSettings(vm); });
            OpenConsoleCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenConsole(vm); });
            OpenPlayersCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenPlayers(vm); });
        }

        public void Activate()
        {
            if (_isActive)
            {
                _listVm.LoadInstances();
                ResolveTunnelsForRunningInstances();
                _ = Task.Run(ResolveAllTunnelsInBackgroundAsync);
                return;
            }

            _registry.InstancesChanged += OnInstancesChanged;
            _lifecycleService.OnInstanceStateChanged += OnInstanceStateChanged;
            _lifecycleService.OnRestartCountdownTick += OnRestartCountdownTick;
            _resourceMonitorService.InstanceMetricsUpdated += OnInstanceMetricsUpdated;
            _resourceMonitorService.GlobalMetricsUpdated += OnGlobalMetricsUpdated;

            _isActive = true;
            _listVm.LoadInstances();
            UpdateAllLiveMetrics();
            ResolveTunnelsForRunningInstances();
            _ = Task.Run(ResolveAllTunnelsInBackgroundAsync);
        }

        public void Deactivate()
        {
            if (!_isActive) return;
            _registry.InstancesChanged -= OnInstancesChanged;
            _lifecycleService.OnInstanceStateChanged -= OnInstanceStateChanged;
            _lifecycleService.OnRestartCountdownTick -= OnRestartCountdownTick;
            _resourceMonitorService.InstanceMetricsUpdated -= OnInstanceMetricsUpdated;
            _resourceMonitorService.GlobalMetricsUpdated -= OnGlobalMetricsUpdated;
            _isActive = false;
        }

        private void OnInstancesChanged(object? sender, EventArgs e) => _dispatcher.Invoke(_listVm.LoadInstances);

        private void OnInstanceStateChanged(Guid instanceId, ServerState state)
        {
            _dispatcher.Invoke(() =>
            {
                var vm = _listVm.GetById(instanceId);
                if (vm == null) return;
                vm.UpdateState(state);
                _metricsVm.ApplyLiveMetrics(vm);
                if (state is ServerState.Starting or ServerState.Online)
                {
                    _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm);
                }
            });
        }

        private void ResolveTunnelsForRunningInstances()
        {
            foreach (var vm in Instances.Where(instance => instance.IsRunning))
            {
                _ = _tunnelOrchestrator.EnsureTunnelFlowAsync(vm);
            }
        }

        private void OnInstanceMetricsUpdated(object? sender, InstanceMetricsUpdatedEventArgs e)
        {
            _dispatcher.InvokeAsync(() =>
            {
                var vm = _listVm.GetById(e.InstanceId);
                if (vm != null)
                {
                    _metricsVm.ApplyLiveMetrics(vm);
                }
            });
        }

        private void OnGlobalMetricsUpdated(object? sender, EventArgs e)
        {
            // Update global metrics if there are any bound properties. 
            // For now, we update all instances to be safe, but we could be more granular.
            _dispatcher.InvokeAsync(UpdateAllLiveMetrics);
        }

        private void OnRestartCountdownTick(Guid instanceId, int secondsRemaining)
        {
            _dispatcher.Invoke(() =>
            {
                var vm = _listVm.GetById(instanceId);
                if (vm == null) return;
                vm.UpdateCountdown(secondsRemaining);
                _metricsVm.ApplyLiveMetrics(vm);
            });
        }

        private void NavigateToNewInstance()
        {
            var page = ActivatorUtilities.CreateInstance<NewInstancePage>(_serviceProvider);
            _navigationService.NavigateToDetailPage(page, "New Server", DetailRouteKind.NewInstance, DetailBackNavigation.Dashboard, true);
        }

        private void NavigateToImportInstance()
        {
            var page = ActivatorUtilities.CreateInstance<InstanceImportPage>(_serviceProvider);
            _navigationService.NavigateToDetailPage(
                page,
                "Import Server",
                DetailRouteKind.InstanceImport,
                DetailBackNavigation.Dashboard,
                true);
        }

        private void UpdateAllLiveMetrics()
        {
            foreach (var vm in Instances) _metricsVm.ApplyLiveMetrics(vm);
        }

        private async Task ResolveAllTunnelsInBackgroundAsync()
        {
            try
            {
                var result = await _playitApiClient.GetTunnelsAsync();
                if (!result.Success || result.Tunnels.Count == 0) return;

                foreach (var vm in Instances)
                {
                    bool isBedrock = vm.IsBedrockServer;
                    int mainPort = vm.Metadata.ServerPort ?? (isBedrock ? 19132 : 25565);

                    // 1. Match Main Port
                    var mainTunnel = result.Tunnels.FirstOrDefault(t =>
                        t.Port == mainPort &&
                        t.TunnelType == (isBedrock ? "minecraft-bedrock" : "minecraft-java"));

                    if (mainTunnel != null && !string.IsNullOrEmpty(mainTunnel.PublicAddress))
                    {
                        _applicationState.SetTunnelAddress(vm.Id, mainTunnel.PublicAddress);
                        if (mainTunnel.NumericAddress != null)
                        {
                            _applicationState.SetNumericTunnelAddress(vm.Id, mainTunnel.NumericAddress);
                        }
                        _dispatcher.Invoke(() =>
                        {
                            vm.TunnelAddress = mainTunnel.PublicAddress;
                            vm.NumericTunnelAddress = mainTunnel.NumericAddress;
                        });
                    }

                    // 2. Match Geyser/Bedrock Port (for Java servers)
                    if (!isBedrock && vm.HasGeyser)
                    {
                        int geyserPort = vm.Metadata.GeyserBedrockPort ?? 19132;
                        var geyserTunnel = result.Tunnels.FirstOrDefault(t =>
                            t.Port == geyserPort &&
                            t.TunnelType == "minecraft-bedrock");

                        if (geyserTunnel != null && !string.IsNullOrEmpty(geyserTunnel.PublicAddress))
                        {
                            _applicationState.SetBedrockTunnelAddress(vm.Id, geyserTunnel.PublicAddress);
                            if (geyserTunnel.NumericAddress != null)
                            {
                                _applicationState.SetBedrockNumericTunnelAddress(vm.Id, geyserTunnel.NumericAddress);
                            }
                            _dispatcher.Invoke(() =>
                            {
                                vm.BedrockTunnelAddress = geyserTunnel.PublicAddress;
                                vm.BedrockNumericTunnelAddress = geyserTunnel.NumericAddress;
                            });
                        }
                    }

                    // 3. Match Simple Voice Chat Port
                    if (vm.Metadata.SimpleVoiceChatDetected && vm.Metadata.SimpleVoiceChatPort.HasValue)
                    {
                        int voicePort = vm.Metadata.SimpleVoiceChatPort.Value;
                        var voiceTunnel = result.Tunnels.FirstOrDefault(t =>
                            t.Port == voicePort &&
                            t.TunnelType == "mc-simple-voice-chat");

                        if (voiceTunnel != null && !string.IsNullOrEmpty(voiceTunnel.PublicAddress))
                        {
                            _applicationState.SetVoiceChatTunnelAddress(vm.Id, voiceTunnel.PublicAddress);
                            if (voiceTunnel.NumericAddress != null)
                            {
                                _applicationState.SetVoiceChatNumericTunnelAddress(vm.Id, voiceTunnel.NumericAddress);
                            }
                            _dispatcher.Invoke(() =>
                            {
                                vm.VoiceChatTunnelAddress = voiceTunnel.PublicAddress;
                                vm.VoiceChatNumericTunnelAddress = voiceTunnel.NumericAddress;
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to pre-resolve offline tunnels on dashboard load: {ex.Message}");
            }
        }
    }
}
