using System;
using System.Collections.ObjectModel;
using System.Linq;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;

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
                if (meta.ServerPort == null)
                {
                    string? path = _registry.GetPath(meta.Id);
                    if (!string.IsNullOrEmpty(path))
                    {
                        string propsFile = System.IO.Path.Combine(path, "server.properties");
                        var props = PocketMC.Desktop.Features.Instances.ServerPropertiesParser.Read(propsFile);
                        if (props.TryGetValue("server-port", out var pPort) && int.TryParse(pPort, out int parsedPort))
                        {
                            meta.ServerPort = parsedPort;
                        }
                    }
                }

                var existing = existingVms.FirstOrDefault(v => v.Id == meta.Id);
                if (existing != null)
                {
                    existing.UpdateFromMetadata(meta);
                    Instances.Add(existing);
                }
                else
                {
                    var newVm = new InstanceCardViewModel(meta, _serverProcessManager, _lifecycleService, _applicationState);
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
