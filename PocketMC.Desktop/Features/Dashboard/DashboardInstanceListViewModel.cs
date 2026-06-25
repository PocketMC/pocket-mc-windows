using System;
using System.Collections.ObjectModel;
using System.Linq;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardInstanceListViewModel : ViewModelBase
    {
        private readonly InstanceRegistry _registry;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly ApplicationState _applicationState;
        private readonly PocketMC.Desktop.Helpers.IGeyserDetector _geyserDetector;
        private readonly PocketMC.Desktop.Features.Networking.ISimpleVoiceChatDetector _voiceChatDetector;

        public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

        public DashboardInstanceListViewModel(
            InstanceRegistry registry,
            ServerProcessManager serverProcessManager,
            IServerLifecycleService lifecycleService,
            ApplicationState applicationState,
            PocketMC.Desktop.Helpers.IGeyserDetector geyserDetector,
            PocketMC.Desktop.Features.Networking.ISimpleVoiceChatDetector voiceChatDetector)
        {
            _registry = registry;
            _serverProcessManager = serverProcessManager;
            _lifecycleService = lifecycleService;
            _applicationState = applicationState;
            _geyserDetector = geyserDetector;
            _voiceChatDetector = voiceChatDetector;
        }

        public void LoadInstances()
        {
            if (!_applicationState.IsConfigured) return;

            var existingVms = Instances.ToList();
            Instances.Clear();
            var metas = _registry.GetAll()
                .OrderByDescending(m => m.PinnedAt.HasValue)
                .ThenBy(m => m.PinnedAt)
                .ThenByDescending(m => m.CreatedAt)
                .ToList();
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
                    var newVm = new InstanceCardViewModel(meta, _serverProcessManager, _lifecycleService, _applicationState, _registry, _geyserDetector, _voiceChatDetector);
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

