using System;
using System.Collections.ObjectModel;
using System.Linq;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Application.Services.Shell;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardInstanceListViewModel : ViewModelBase
    {
        private readonly InstanceRegistry _registry;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly ApplicationState _applicationState;
        private readonly PocketMC.Application.Interfaces.Instances.IGeyserDetector _geyserDetector;
        private readonly PocketMC.Application.Services.Networking.ISimpleVoiceChatDetector _voiceChatDetector;
        private readonly PocketMC.Application.Interfaces.Networking.ILocalNetworkAddressService _localNetworkAddressService;

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

        public DashboardInstanceListViewModel(
            InstanceRegistry registry,
            ServerProcessManager serverProcessManager,
            IServerLifecycleService lifecycleService,
            ApplicationState applicationState,
            PocketMC.Application.Interfaces.Instances.IGeyserDetector geyserDetector,
            PocketMC.Application.Services.Networking.ISimpleVoiceChatDetector voiceChatDetector,
            PocketMC.Application.Interfaces.Networking.ILocalNetworkAddressService? localNetworkAddressService = null)
        {
            _registry = registry;
            _serverProcessManager = serverProcessManager;
            _lifecycleService = lifecycleService;
            _applicationState = applicationState;
            _geyserDetector = geyserDetector;
            _voiceChatDetector = voiceChatDetector;
            _localNetworkAddressService = localNetworkAddressService ?? new PocketMC.Infrastructure.Networking.LocalNetworkAddressService();
        }

        public async Task LoadInstancesAsync()
        {
            if (!_applicationState.IsConfigured)
            {
                IsLoading = false;
                return;
            }

            if (Instances.Count == 0)
            {
                IsLoading = true;
            }

            var metas = await Task.Run(() =>
            {
                var list = _registry.GetAll()
                    .OrderByDescending(m => m.PinnedAt.HasValue)
                    .ThenBy(m => m.PinnedAt)
                    .ThenByDescending(m => m.CreatedAt)
                    .ToList();

                foreach (var meta in list)
                {
                    if (meta.ServerPort == null)
                    {
                        string? path = _registry.GetPath(meta.Id);
                        if (!string.IsNullOrEmpty(path))
                        {
                            string propsFile = System.IO.Path.Combine(path, "server.properties");
                            if (System.IO.File.Exists(propsFile))
                            {
                                var props = PocketMC.Application.Services.Instances.ServerPropertiesParser.Read(propsFile);
                                if (props.TryGetValue("server-port", out var pPort) && int.TryParse(pPort, out int parsedPort))
                                {
                                    meta.ServerPort = parsedPort;
                                }
                            }
                        }
                    }
                }
                return list;
            });

            if (metas.Count == 0)
            {
                Instances.Clear();
                IsLoading = false;
                return;
            }

            var existingVms = Instances.ToList();
            var updatedVms = new System.Collections.Generic.List<InstanceCardViewModel>();

            foreach (var meta in metas)
            {
                var existing = existingVms.FirstOrDefault(v => v.Id == meta.Id);
                if (existing != null)
                {
                    existing.UpdateFromMetadata(meta);
                    updatedVms.Add(existing);
                }
                else
                {
                    var newVm = new InstanceCardViewModel(meta, _serverProcessManager, _lifecycleService, _applicationState, _registry, _geyserDetector, _voiceChatDetector, _localNetworkAddressService);
                    updatedVms.Add(newVm);
                }
            }

            foreach (var vm in updatedVms)
            {
                var process = _serverProcessManager.GetProcess(vm.Id);
                if (process != null) vm.UpdateState(process.State);
            }

            // Sync Instances collection smoothly
            bool hasChanged = Instances.Count != updatedVms.Count || !Instances.SequenceEqual(updatedVms);
            if (hasChanged)
            {
                Instances.Clear();
                foreach (var vm in updatedVms)
                {
                    Instances.Add(vm);
                }
            }

            IsLoading = false;
        }

        public void LoadInstances()
        {
            _ = LoadInstancesAsync();
        }

        public InstanceCardViewModel? GetById(Guid id) => Instances.FirstOrDefault(i => i.Id == id);
    }
}

