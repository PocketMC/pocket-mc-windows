using System;
using System.Collections.ObjectModel;
using System.Linq;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardInstanceListVM : ViewModelBase
    {
        private readonly InstanceRegistry _registry;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly ApplicationState _applicationState;

        public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

        public DashboardInstanceListVM(
            InstanceRegistry registry,
            ServerProcessManager serverProcessManager,
            IServerLifecycleService lifecycleService,
            ApplicationState applicationState)
        {
            _registry = registry;
            _serverProcessManager = serverProcessManager;
            _lifecycleService = lifecycleService;
            _applicationState = applicationState;
        }

        public void LoadInstances()
        {
            if (!_applicationState.IsConfigured) return;

            var existingVms = Instances.ToList();
            Instances.Clear();
            var metas = _registry.GetAll();
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
                    var newVm = new InstanceCardViewModel(meta, _serverProcessManager, _lifecycleService);
                    Instances.Add(newVm);
                }
            }

            foreach (var vm in Instances)
            {
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process != null) vm.UpdateState(process.State);
            }
        }

        public InstanceCardViewModel? GetById(Guid id) => Instances.FirstOrDefault(i => i.Id == id);
    }
}
