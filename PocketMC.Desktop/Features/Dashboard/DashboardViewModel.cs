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
using PocketMC.Desktop.Features.Instances.Backups;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly DashboardInstanceListVM _listVm;
        private readonly DashboardMetricsVM _metricsVm;
        private readonly DashboardActionsVM _actionsVm;

        private readonly InstanceRegistry _registry;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly IResourceMonitorService _resourceMonitorService;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;

        private bool _isActive;

        public ObservableCollection<InstanceCardViewModel> Instances => _listVm.Instances;
        public ICommand NewInstanceCommand { get; }
        public ICommand RefreshInstancesCommand { get; }
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand RestartServerCommand { get; }
        public ICommand DeleteInstanceCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyCrashReportCommand { get; }
        public ICommand ServerSettingsCommand { get; }
        public ICommand OpenConsoleCommand { get; }
        public ICommand OpenPlayersCommand { get; }
        public DashboardViewModel(
            DashboardInstanceListVM listVm,
            DashboardMetricsVM metricsVm,
            DashboardActionsVM actionsVm,
            InstanceRegistry registry,
            IServerLifecycleService lifecycleService,
            IResourceMonitorService resourceMonitorService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider)
        {
            _listVm = listVm;
            _metricsVm = metricsVm;
            _actionsVm = actionsVm;
            _registry = registry;
            _lifecycleService = lifecycleService;
            _resourceMonitorService = resourceMonitorService;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;

            NewInstanceCommand = new RelayCommand(_ => NavigateToNewInstance());
            RefreshInstancesCommand = new RelayCommand(_ => _listVm.LoadInstances());
            StartServerCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.StartServer(vm, _metricsVm.ApplyLiveMetrics); });
            StopServerCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.StopServer(vm, _metricsVm.ApplyLiveMetrics); });
            RestartServerCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.RestartServer(vm, _metricsVm.ApplyLiveMetrics); });
            DeleteInstanceCommand = new AsyncRelayCommand(async p => { if (p is InstanceCardViewModel vm) await _actionsVm.DeleteInstanceAsync(vm); });
            OpenFolderCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenFolder(vm); });
            CopyCrashReportCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.CopyCrashReport(vm); });
            ServerSettingsCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenSettings(vm); });
            OpenConsoleCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenConsole(vm); });
            OpenPlayersCommand = new RelayCommand(p => { if (p is InstanceCardViewModel vm) _actionsVm.OpenPlayers(vm); });
        }

        public void Activate()
        {
            if (_isActive) { _listVm.LoadInstances(); return; }

            _registry.InstancesChanged += OnInstancesChanged;
            _lifecycleService.OnInstanceStateChanged += OnInstanceStateChanged;
            _lifecycleService.OnRestartCountdownTick += OnRestartCountdownTick;
            _resourceMonitorService.InstanceMetricsUpdated += OnInstanceMetricsUpdated;
            _resourceMonitorService.GlobalMetricsUpdated += OnGlobalMetricsUpdated;

            _isActive = true;
            _listVm.LoadInstances();
            UpdateAllLiveMetrics();
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
            });
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
            _navigationService.NavigateToDetailPage(page, "New Instance", DetailRouteKind.NewInstance, DetailBackNavigation.Dashboard, true);
        }

        private void UpdateAllLiveMetrics()
        {
            foreach (var vm in Instances) _metricsVm.ApplyLiveMetrics(vm);
        }
    }
}
