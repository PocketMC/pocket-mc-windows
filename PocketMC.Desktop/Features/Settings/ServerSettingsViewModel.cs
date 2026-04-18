using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Features.Settings
{
    public class ServerSettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly InstanceManager _instanceManager;
        private readonly InstanceRegistry _registry;
        private readonly ServerConfigurationService _serverConfigurationService;
        private readonly PortPreflightService _portPreflightService;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly Action<Guid, ServerState> _instanceStateChangedHandler;
        private readonly string _appRootPath;
        private readonly ApplicationState _applicationState;

        public InstanceMetadata Metadata { get; }
        public string ServerDir { get; }

        // Sub-ViewModels
        public SettingsGeneralVM General { get; }
        public SettingsWorldVM World { get; }
        public SettingsPerformanceVM Performance { get; }
        public SettingsBackupsVM Backups { get; }
        public SettingsAddonsVM Addons { get; }
        public SettingsAdvancedVM Advanced { get; }
        public SettingsSummariesVM Summaries { get; }

        private bool _isAiSummarizationAvailable;
        public bool IsAiSummarizationAvailable { get => _isAiSummarizationAvailable; set => SetProperty(ref _isAiSummarizationAvailable, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        private bool _isTransientState;
        public bool IsTransientState { get => _isTransientState; set => SetProperty(ref _isTransientState, value); }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges { get => _hasUnsavedChanges; set => SetProperty(ref _hasUnsavedChanges, value); }

        private bool _isRestartRequired;
        public bool IsRestartRequired { get => _isRestartRequired; set => SetProperty(ref _isRestartRequired, value); }

        private string _playitAddress = "Resolving tunnel...";
        public string PlayitAddress { get => _playitAddress; set => SetProperty(ref _playitAddress, value); }

        private string _playitBedrockAddress = "Resolving Bedrock tunnel...";
        public string PlayitBedrockAddress { get => _playitBedrockAddress; set => SetProperty(ref _playitBedrockAddress, value); }

        public bool HasGeyser => Metadata.HasGeyser;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ResolvePlayitCommand { get; }

        public ServerSettingsViewModel(
            InstanceMetadata metadata,
            InstanceManager instanceManager,
            InstanceRegistry registry,
            ServerConfigurationService serverConfigurationService,
            PortPreflightService portPreflightService,
            IServerLifecycleService lifecycleService,
            WorldManager worldManager,
            BackupService backupService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            ModpackService modpackService,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider)
        {
            Metadata = metadata;
            _instanceManager = instanceManager;
            _registry = registry;
            _serverConfigurationService = serverConfigurationService;
            _portPreflightService = portPreflightService;
            _lifecycleService = lifecycleService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _applicationState = (ApplicationState)serviceProvider.GetService(typeof(ApplicationState))!;
            _appRootPath = _applicationState.GetRequiredAppRootPath();
            ServerDir = _registry.GetPath(metadata.Id) ?? throw new InvalidOperationException();

            _instanceStateChangedHandler = (id, state) => { if (id == Metadata.Id) dispatcher.Invoke(UpdateRunningState); };
            _lifecycleService.OnInstanceStateChanged += _instanceStateChangedHandler;

            var updateService = (UpdateService)serviceProvider.GetService(typeof(UpdateService))!;
            General = new SettingsGeneralVM(ServerDir, updateService, dialogService, navigationService, MarkChanged);
            World = new SettingsWorldVM(ServerDir, worldManager, dialogService, dispatcher, navigationService, serviceProvider, metadata.MinecraftVersion, () => IsRunning, MarkChanged);
            Performance = new SettingsPerformanceVM(dialogService, MarkChanged);
            Backups = new SettingsBackupsVM(metadata, ServerDir, backupService, dialogService, dispatcher, () => IsRunning, MarkChanged);
            Addons = new SettingsAddonsVM(metadata, ServerDir, modpackService, dialogService, navigationService, serviceProvider, () => IsRunning, MarkChanged);
            Advanced = new SettingsAdvancedVM(ServerDir, serverConfigurationService, MarkChanged);

            var summaryStorage = (SummaryStorageService)serviceProvider.GetService(typeof(SummaryStorageService))!;
            Summaries = new SettingsSummariesVM(ServerDir, summaryStorage, dialogService);

            SaveCommand = new RelayCommand(_ => SaveConfigurations(), _ => !IsTransientState);
            CancelCommand = new RelayCommand(async _ => await CancelAsync());
            ResolvePlayitCommand = new RelayCommand(_ => _ = ResolveTunnelAddressAsync(playitApiClient));

            LoadAll(playitApiClient);
        }

        public void LoadAll(PlayitApiClient playitApiClient)
        {
            IsLoading = true;
            UpdateRunningState();

            var cfg = _serverConfigurationService.Load(Metadata, ServerDir);

            // General
            General.Motd = cfg.Motd;
            General.ServerPort = cfg.ServerPort;
            General.ServerIp = cfg.ServerIp;
            General.GeyserBedrockPort = Metadata.GeyserBedrockPort?.ToString() ?? "19132";
            General.LoadIcon();

            // World
            World.Seed = cfg.Seed;
            World.LevelType = cfg.LevelType;
            World.Gamemode = cfg.Gamemode;
            World.Difficulty = cfg.Difficulty;
            World.Pvp = cfg.Pvp;
            World.WhiteList = cfg.WhiteList;
            World.OnlineMode = cfg.OnlineMode;
            World.AllowFlight = cfg.AllowFlight;
            World.AllowNether = cfg.AllowNether;
            World.AllowCommandBlock = cfg.AllowCommandBlock;
            World.MaxPlayers = cfg.MaxPlayers;
            World.SpawnProtection = cfg.SpawnProtection;
            World.LoadWorldState();

            // Performance
            Performance.MinRam = cfg.MinRamMb;
            Performance.MaxRam = cfg.MaxRamMb;
            Performance.JavaPath = cfg.CustomJavaPath;
            Performance.AdvancedJvmArgs = cfg.AdvancedJvmArgs;

            // Advanced
            Advanced.EnableAutoRestart = cfg.EnableAutoRestart;
            Advanced.MaxAutoRestarts = cfg.MaxAutoRestarts.ToString();
            Advanced.AutoRestartDelay = cfg.AutoRestartDelaySeconds.ToString();
            Advanced.LoadRawProperties();
            Advanced.AdvancedProperties.Clear();
            foreach (var kvp in cfg.AllProperties) Advanced.AdvancedProperties.Add(Advanced.CreatePropertyItem(kvp.Key, kvp.Value));

            Addons.LoadAddons();
            Backups.LoadBackups();

            // AI Summaries
            bool hasApiKey = !string.IsNullOrWhiteSpace(_applicationState.Settings.GetCurrentAiKey());
            IsAiSummarizationAvailable = hasApiKey;
            Summaries.Load(hasApiKey);

            _ = ResolveTunnelAddressAsync(playitApiClient);

            IsLoading = false;
            string? initialJavaPath = cfg.CustomJavaPath;
            if (string.IsNullOrWhiteSpace(initialJavaPath))
            {
                int requiredVersion = PocketMC.Desktop.Features.Java.JavaRuntimeResolver.GetRequiredJavaVersion(Metadata.MinecraftVersion);
                initialJavaPath = PocketMC.Desktop.Features.Java.JavaRuntimeResolver.GetExpectedBundledJavaPath(_appRootPath, requiredVersion);
            }
            Performance.JavaPath = initialJavaPath;
            HasUnsavedChanges = false;
        }

        private void UpdateRunningState()
        {
            bool running = _lifecycleService.IsRunning(Metadata.Id);
            bool waiting = _lifecycleService.IsWaitingToRestart(Metadata.Id);

            IsRunning = running;
            IsTransientState = waiting;

            if (!running && !waiting)
            {
                IsRestartRequired = false;
            }
        }

        private void MarkChanged() { if (!IsLoading) HasUnsavedChanges = true; }

        private async Task ResolveTunnelAddressAsync(PlayitApiClient client)
        {
            if (!int.TryParse(General.ServerPort, out int port))
            {
                PlayitAddress = "⚠ Invalid port number.";
                if (HasGeyser) PlayitBedrockAddress = "⚠ Invalid port number.";
                return;
            }
            PlayitAddress = "⏳ Resolving tunnel...";
            if (HasGeyser) PlayitBedrockAddress = "⏳ Resolving Bedrock tunnel...";
            try
            {
                IReadOnlyList<PortCheckRequest> requests = BuildPortRequestsForSettings(port);
                PortCheckRequest primaryRequest = requests.FirstOrDefault(IsPrimaryTunnelRequest)
                    ?? new PortCheckRequest(port, GetDefaultProtocol(Metadata), instanceId: Metadata.Id, instanceName: Metadata.Name, instancePath: ServerDir);
                PortCheckRequest? geyserRequest = requests.FirstOrDefault(IsGeyserBedrockRequest);

                var result = await client.GetTunnelsAsync();
                if (!result.Success)
                {
                    string failureText = BuildPlayitFailureText(result);
                    PlayitAddress = failureText;
                    if (HasGeyser) PlayitBedrockAddress = failureText;
                    return;
                }
                var match = PlayitApiClient.FindTunnelForRequest(result.Tunnels, primaryRequest);
                PlayitAddress = match != null
                    ? match.PublicAddress
                    : $"No {FormatProtocol(primaryRequest.Protocol)} tunnel found for port {primaryRequest.Port}. Please create one.";

                if (HasGeyser)
                {
                    int geyserPort = Metadata.GeyserBedrockPort ?? 19132;
                    geyserRequest ??= new PortCheckRequest(
                        geyserPort,
                        PortProtocol.Udp,
                        PortIpMode.IPv4,
                        instanceId: Metadata.Id,
                        instanceName: Metadata.Name,
                        instancePath: ServerDir,
                        bindingRole: PortBindingRole.GeyserBedrock,
                        engine: PortEngine.Geyser,
                        displayName: "Geyser Bedrock");

                    var bedrockMatch = PlayitApiClient.FindTunnelForRequest(result.Tunnels, geyserRequest);
                    PlayitBedrockAddress = bedrockMatch != null
                        ? bedrockMatch.PublicAddress
                        : $"No Bedrock UDP tunnel found for port {geyserRequest.Port}. Please create one.";
                }
            }
            catch
            {
                PlayitAddress = "⚠ Connection failed. Check your internet.";
                if (HasGeyser) PlayitBedrockAddress = "⚠ Connection failed. Check your internet.";
            }
        }

        private IReadOnlyList<PortCheckRequest> BuildPortRequestsForSettings(int currentPrimaryPort)
        {
            try
            {
                var requests = _portPreflightService.BuildRequests(Metadata, ServerDir).ToArray();
                PortCheckRequest? primary = requests.FirstOrDefault(IsPrimaryTunnelRequest);
                if (primary == null || primary.Port == currentPrimaryPort)
                {
                    return requests;
                }

                return requests
                    .Select(request => ReferenceEquals(request, primary)
                        ? new PortCheckRequest(
                            currentPrimaryPort,
                            request.Protocol,
                            request.IpMode,
                            request.BindAddress,
                            request.InstanceId,
                            request.InstanceName,
                            request.InstancePath,
                            request.CheckTunnelAvailability,
                            request.CheckPublicReachability,
                            request.BindingRole,
                            request.Engine,
                            request.DisplayName)
                        : request)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<PortCheckRequest>();
            }
        }

        private static bool IsPrimaryTunnelRequest(PortCheckRequest request)
        {
            return request.BindingRole != PortBindingRole.GeyserBedrock &&
                   request.IpMode != PortIpMode.IPv6;
        }

        private static bool IsGeyserBedrockRequest(PortCheckRequest request)
        {
            return request.BindingRole == PortBindingRole.GeyserBedrock ||
                   request.Engine == PortEngine.Geyser;
        }

        private static PortProtocol GetDefaultProtocol(InstanceMetadata metadata)
        {
            return metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true ||
                   metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true
                ? PortProtocol.Udp
                : PortProtocol.Tcp;
        }

        private static string BuildPlayitFailureText(TunnelListResult result)
        {
            if (result.RequiresClaim)
            {
                return "⚠ Finish Playit claim.";
            }

            if (result.IsTokenInvalid)
            {
                return "⚠ Reconnect Playit.";
            }

            return "⚠ Failed to reach Playit API.";
        }

        private static string FormatProtocol(PortProtocol protocol)
        {
            return protocol switch
            {
                PortProtocol.Udp => "UDP",
                PortProtocol.TcpAndUdp => "TCP/UDP",
                _ => "TCP"
            };
        }

        private void SaveConfigurations()
        {
            string? customJavaPathToSave = Performance.JavaPath;
            if (string.IsNullOrWhiteSpace(customJavaPathToSave))
            {
                int requiredVersion = PocketMC.Desktop.Features.Java.JavaRuntimeResolver.GetRequiredJavaVersion(Metadata.MinecraftVersion);
                customJavaPathToSave = PocketMC.Desktop.Features.Java.JavaRuntimeResolver.GetExpectedBundledJavaPath(_appRootPath, requiredVersion);
            }

            var cfg = new ServerConfiguration
            {
                MinRamMb = (int)Performance.MinRam,
                MaxRamMb = (int)Performance.MaxRam,
                CustomJavaPath = customJavaPathToSave,
                AdvancedJvmArgs = Performance.AdvancedJvmArgs,
                EnableAutoRestart = Advanced.EnableAutoRestart,
                MaxAutoRestarts = int.TryParse(Advanced.MaxAutoRestarts, out int mr) ? mr : Metadata.MaxAutoRestarts,
                AutoRestartDelaySeconds = int.TryParse(Advanced.AutoRestartDelay, out int rd) ? rd : Metadata.AutoRestartDelaySeconds,
                BackupIntervalHours = Backups.BackupIntervalHours,
                MaxBackupsToKeep = Backups.MaxBackupsToKeep,
                Motd = General.Motd ?? "",
                Seed = World.Seed ?? "",
                SpawnProtection = World.SpawnProtection,
                MaxPlayers = World.MaxPlayers,
                ServerPort = General.ServerPort,
                ServerIp = General.ServerIp ?? "",
                LevelType = World.LevelType,
                OnlineMode = World.OnlineMode,
                Pvp = World.Pvp,
                WhiteList = World.WhiteList,
                Gamemode = World.Gamemode,
                Difficulty = World.Difficulty,
                AllowCommandBlock = World.AllowCommandBlock,
                AllowFlight = World.AllowFlight,
                AllowNether = World.AllowNether
            };

            foreach (var item in Advanced.AdvancedProperties)
            {
                if (!string.IsNullOrWhiteSpace(item.Key) && (!ServerConfigurationService.IsCoreProperty(item.Key) || item.IsDirty))
                    cfg.AdvancedProperties[item.Key] = item.Value;
            }

            Metadata.GeyserBedrockPort = int.TryParse(General.GeyserBedrockPort, out int gPort) ? gPort : 19132;
            _serverConfigurationService.Save(Metadata, ServerDir, cfg);
            if (Advanced.IsRawServerPropertiesDirty)
            {
                _serverConfigurationService.SaveRawProperties(ServerDir, Advanced.RawServerProperties);
                Advanced.ClearDirtyRaw();
            }

            if (IsRunning)
            {
                IsRestartRequired = true;
            }
            else
            {
                IsRestartRequired = false;
            }

            HasUnsavedChanges = false;
            _dialogService.ShowMessage("Saved", "Configurations saved successfully.");
        }

        private async Task CancelAsync()
        {
            if (HasUnsavedChanges && await _dialogService.ShowDialogAsync("Discard Changes", "You have unsaved changes. Discard them?", DialogType.Warning, false) != DialogResult.Yes) return;
            if (!_navigationService.NavigateBack()) _navigationService.NavigateToDashboard();
        }

        public void Dispose() => _lifecycleService.OnInstanceStateChanged -= _instanceStateChangedHandler;
    }
}
