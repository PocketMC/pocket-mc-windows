using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PortsMapPage : Page
    {
        private readonly InstanceRegistry _instanceRegistry;
        private readonly IServerLifecycleService _lifecycleService;
        private readonly PortLeaseRegistry _portLeaseRegistry;
        private readonly PlayitApiClient _playitApiClient;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly InstanceManager _instanceManager;
        private readonly ServerConfigurationService _serverConfigurationService;
        private readonly ILogger<PortsMapPage> _logger;

        private static readonly SolidColorBrush ActiveGreenBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly SolidColorBrush WarningYellowBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
        private static readonly SolidColorBrush OfflineGreyBrush = CreateFrozenBrush(Color.FromRgb(0x88, 0x88, 0x88));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public ICommand CopyCommand { get; }
        public ICommand NavigateToConsoleCommand { get; }
        public ICommand EditPortCommand { get; }
        public ICommand SavePortCommand { get; }
        public ICommand CancelPortCommand { get; }

        public PortsMapPage(
            InstanceRegistry instanceRegistry,
            IServerLifecycleService lifecycleService,
            PortLeaseRegistry portLeaseRegistry,
            PlayitApiClient playitApiClient,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider,
            InstanceManager instanceManager,
            ServerConfigurationService serverConfigurationService,
            ILogger<PortsMapPage> logger)
        {
            InitializeComponent();
            _instanceRegistry = instanceRegistry;
            _lifecycleService = lifecycleService;
            _portLeaseRegistry = portLeaseRegistry;
            _playitApiClient = playitApiClient;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;
            _instanceManager = instanceManager;
            _serverConfigurationService = serverConfigurationService;
            _logger = logger;

            DataContext = this;

            CopyCommand = new RelayCommand(ExecuteCopyAddress);
            NavigateToConsoleCommand = new RelayCommand(ExecuteNavigateToConsole);
            EditPortCommand = new RelayCommand(ExecuteEditPort);
            SavePortCommand = new RelayCommand(ExecuteSavePort);
            CancelPortCommand = new RelayCommand(ExecuteCancelPort);

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RefreshMapAsync();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (!_navigationService.NavigateBack())
            {
                _navigationService.NavigateToTunnel();
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshMapAsync();
        }

        private void ExecuteCopyAddress(object? parameter)
        {
            if (parameter is string address && !string.IsNullOrEmpty(address))
            {
                try
                {
                    Clipboard.SetText(address);
                    _logger.LogInformation("Copied public tunnel address to clipboard: {Address}", address);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to copy tunnel address to clipboard.");
                }
            }
        }

        private void ExecuteNavigateToConsole(object? parameter)
        {
            if (parameter is Guid serverId)
            {
                var server = _instanceRegistry.GetById(serverId);
                if (server == null) return;

                var process = _lifecycleService.GetProcess(serverId);
                if (process != null)
                {
                    var consolePage = ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, server, process);
                    _navigationService.NavigateToDetailPage(consolePage, $"Console: {server.Name}", DetailRouteKind.ServerConsole, DetailBackNavigation.Tunnel, true);
                }
                else
                {
                    var settingsViewModel = ActivatorUtilities.CreateInstance<ServerSettingsViewModel>(_serviceProvider, server);
                    var settingsPage = ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, settingsViewModel);
                    _navigationService.NavigateToDetailPage(settingsPage, $"Settings: {server.Name}", DetailRouteKind.ServerSettings, DetailBackNavigation.Tunnel, true);
                }
            }
        }

        private void ExecuteEditPort(object? parameter)
        {
            if (parameter is RouteViewModel route)
            {
                if (route.IsOnline)
                {
                    AppDialog.ShowWarning("Server is Running", "Cannot change port while the server is running. Please stop the server first.");
                    return;
                }

                // Close editing on any other routes first to keep it clean
                var groups = RoutesList.ItemsSource as List<ServerGroupViewModel>;
                if (groups != null)
                {
                    foreach (var g in groups)
                    {
                        foreach (var r in g.Routes)
                        {
                            if (r != route) r.IsEditingPort = false;
                        }
                    }
                }

                route.EditingPortText = route.Port.ToString();
                route.IsEditingPort = true;
            }
        }

        private void ExecuteCancelPort(object? parameter)
        {
            if (parameter is RouteViewModel route)
            {
                route.IsEditingPort = false;
            }
        }

        private async void ExecuteSavePort(object? parameter)
        {
            if (parameter is RouteViewModel route)
            {
                if (!int.TryParse(route.EditingPortText, out int newPort) || newPort < 1 || newPort > 65535)
                {
                    AppDialog.ShowError("Invalid Port", "Please enter a valid port number between 1 and 65535.");
                    return;
                }

                if (route.IsOnline)
                {
                    AppDialog.ShowWarning("Server is Running", "Cannot change port while the server is running. Please stop the server first.");
                    return;
                }

                // Check for port collision robustly across the entire ecosystem
                var allServers = _instanceRegistry.GetAll();
                foreach (var s in allServers)
                {
                    if (s.ServerPort == newPort && (s.Id != route.ServerId || route.PortType != "Main"))
                    {
                        AppDialog.ShowError("Port Collision", $"Port {newPort} is already in use as the main port for server '{s.Name}'.");
                        return;
                    }
                    if (s.HasGeyser && s.GeyserBedrockPort == newPort && (s.Id != route.ServerId || route.PortType != "Geyser"))
                    {
                        AppDialog.ShowError("Port Collision", $"Port {newPort} is already in use as the Geyser bedrock bridge port for server '{s.Name}'.");
                        return;
                    }
                    if (s.SimpleVoiceChatDetected && s.SimpleVoiceChatPort == newPort && (s.Id != route.ServerId || route.PortType != "Voice"))
                    {
                        AppDialog.ShowError("Port Collision", $"Port {newPort} is already in use as the Simple Voice Chat port for server '{s.Name}'.");
                        return;
                    }
                }

                try
                {
                    var server = _instanceRegistry.GetById(route.ServerId);
                    var serverDir = _instanceRegistry.GetPath(route.ServerId);
                    if (server != null && !string.IsNullOrEmpty(serverDir))
                    {
                        if (route.PortType == "Main")
                        {
                            var cfg = _serverConfigurationService.Load(server, serverDir);
                            cfg.ServerPort = newPort.ToString();
                            _serverConfigurationService.Save(server, serverDir, cfg);
                        }
                        else if (route.PortType == "Geyser")
                        {
                            server.GeyserBedrockPort = newPort;
                            _instanceManager.SaveMetadata(server, serverDir);
                        }
                        else if (route.PortType == "Voice")
                        {
                            server.SimpleVoiceChatPort = newPort;
                            _instanceManager.SaveMetadata(server, serverDir);
                            
                            // Patch voicechat properties file robustly
                            string configPath = SimpleVoiceChatConfigService.DetectConfigPath(serverDir);
                            if (File.Exists(configPath))
                            {
                                SimpleVoiceChatConfigService.PatchPortIfNeeded(configPath, newPort);
                            }
                        }

                        route.Port = newPort;
                        route.LocalPortLabel = $"{newPort} / {route.Protocol}";
                        route.IsEditingPort = false;

                        AppDialog.ShowInfo("Port Updated", $"Successfully updated {route.ServiceRoleName} port to {newPort}.");
                        await RefreshMapAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update port.");
                    AppDialog.ShowError("Error Updating Port", $"An error occurred while saving the port configuration: {ex.Message}");
                }
            }
        }

        private async Task RefreshMapAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            NoRoutesPanel.Visibility = Visibility.Collapsed;
            RoutesList.ItemsSource = null;

            try
            {
                var tunnelResult = await Task.Run(() => _playitApiClient.GetTunnelsAsync());
                List<TunnelData> activeTunnels = tunnelResult?.Success == true
                    ? tunnelResult.Tunnels
                    : new List<TunnelData>();

                var servers = _instanceRegistry.GetAll();
                var groupsList = new List<ServerGroupViewModel>();

                foreach (var server in servers)
                {
                    bool isRunning = _lifecycleService.IsRunning(server.Id);
                    ServerProcess? process = _lifecycleService.GetProcess(server.Id);
                    ServerState state = process?.State ?? (isRunning ? ServerState.Online : ServerState.Stopped);

                    string serverInfo;
                    string playerMetrics;
                    Brush activeColor;
                    bool isOnline;
                    bool isExpanded;
                    string actionBtnText;
                    Wpf.Ui.Controls.SymbolRegular actionBtnIcon;

                    if (state == ServerState.Online)
                    {
                        serverInfo = $"{server.ServerType} {server.MinecraftVersion} - Online";
                        playerMetrics = $"{process?.PlayerCount ?? 0} / {server.MaxPlayers} Players";
                        activeColor = ActiveGreenBrush;
                        isOnline = true;
                        isExpanded = true;
                        actionBtnText = "View Console";
                        actionBtnIcon = Wpf.Ui.Controls.SymbolRegular.Code24;
                    }
                    else if (state is ServerState.Starting or ServerState.SettingUp or ServerState.Installing or ServerState.Stopping)
                    {
                        serverInfo = $"{server.ServerType} {server.MinecraftVersion} - {state}";
                        playerMetrics = "Connecting...";
                        activeColor = WarningYellowBrush;
                        isOnline = true;
                        isExpanded = true;
                        actionBtnText = "View Console";
                        actionBtnIcon = Wpf.Ui.Controls.SymbolRegular.Code24;
                    }
                    else if (state == ServerState.Crashed)
                    {
                        serverInfo = $"{server.ServerType} {server.MinecraftVersion} - Crashed";
                        playerMetrics = string.Empty;
                        activeColor = WarningYellowBrush;
                        isOnline = false;
                        isExpanded = true;
                        actionBtnText = "Manage Server";
                        actionBtnIcon = Wpf.Ui.Controls.SymbolRegular.Settings24;
                    }
                    else
                    {
                        serverInfo = $"{server.ServerType} {server.MinecraftVersion} - Offline";
                        playerMetrics = string.Empty;
                        activeColor = OfflineGreyBrush;
                        isOnline = false;
                        isExpanded = false;
                        actionBtnText = "Manage Server";
                        actionBtnIcon = Wpf.Ui.Controls.SymbolRegular.Settings24;
                    }

                    var serverGroup = new ServerGroupViewModel
                    {
                        ServerId = server.Id,
                        ServerName = server.Name,
                        ServerInfo = serverInfo,
                        PlayerMetrics = playerMetrics,
                        ActiveColor = activeColor,
                        IsOnline = isOnline,
                        IsExpanded = isExpanded,
                        ActionButtonText = actionBtnText,
                        ActionButtonIcon = actionBtnIcon
                    };

                    var ports = new List<(int Port, PortProtocol Protocol, string Name)>();
                    bool isBedrock = server.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true ||
                                     server.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;

                    var leases = _portLeaseRegistry.GetAllLeases().Where(l => l.InstanceId == server.Id).ToList();
                    if (leases.Count > 0)
                    {
                        foreach (var lease in leases)
                        {
                            string portName = lease.Protocol == PortProtocol.Udp ? "UDP Port" : "TCP Port";
                            if (lease.Port == server.ServerPort)
                            {
                                portName = isBedrock ? "Bedrock/UDP" : "Java/TCP";
                            }
                            else if (lease.Port == server.GeyserBedrockPort && server.HasGeyser)
                            {
                                portName = "Geyser/UDP";
                            }
                            else if (lease.Port == server.SimpleVoiceChatPort && server.SimpleVoiceChatDetected)
                            {
                                portName = "Voice/UDP";
                            }
                            ports.Add((lease.Port, lease.Protocol, portName));
                        }
                    }
                    else
                    {
                        int mainPort = server.ServerPort ?? (isBedrock ? 19132 : 25565);
                        ports.Add((mainPort, isBedrock ? PortProtocol.Udp : PortProtocol.Tcp, isBedrock ? "Bedrock/UDP" : "Java/TCP"));

                        if (server.HasGeyser)
                        {
                            ports.Add((server.GeyserBedrockPort ?? 19132, PortProtocol.Udp, "Geyser/UDP"));
                        }

                        if (server.SimpleVoiceChatDetected && server.SimpleVoiceChatPort.HasValue)
                        {
                            ports.Add((server.SimpleVoiceChatPort.Value, PortProtocol.Udp, "Voice/UDP"));
                        }
                    }

                    var uniquePorts = new List<(int Port, PortProtocol Protocol, string Name)>();
                    foreach (var portEntry in ports)
                    {
                        if (!uniquePorts.Any(p => p.Port == portEntry.Port && p.Protocol == portEntry.Protocol))
                        {
                            uniquePorts.Add(portEntry);
                        }
                    }

                    foreach (var p in uniquePorts)
                    {
                        var tunnel = activeTunnels.FirstOrDefault(t => 
                            t.Port == p.Port && 
                            (!t.Protocol.HasValue || t.Protocol == p.Protocol || t.Protocol == PortProtocol.TcpAndUdp));

                        string roleName;
                        string roleDetail;
                        string portType = "Main";

                        if (p.Port == server.ServerPort || p.Name.Contains("Java/TCP") || p.Name.Contains("Bedrock/UDP"))
                        {
                            roleName = isBedrock ? "Minecraft Bedrock Port" : "Minecraft Java Port";
                            roleDetail = isBedrock ? "Native game server entrypoint" : "Standard Java server endpoint";
                            portType = "Main";
                        }
                        else if (p.Port == server.GeyserBedrockPort && server.HasGeyser)
                        {
                            roleName = "Geyser Bedrock Bridge";
                            roleDetail = "Allows Bedrock players to join Java";
                            portType = "Geyser";
                        }
                        else if (p.Port == server.SimpleVoiceChatPort && server.SimpleVoiceChatDetected)
                        {
                            roleName = "Simple Voice Chat";
                            roleDetail = "Proximity audio communications link";
                            portType = "Voice";
                        }
                        else
                        {
                            roleName = p.Name;
                            roleDetail = "Custom game server mapping";
                            portType = "Main";
                        }

                        var route = new RouteViewModel
                        {
                            PublicAddressLabel = tunnel != null && !string.IsNullOrEmpty(tunnel.PublicAddress) 
                                ? tunnel.PublicAddress 
                                : (tunnel != null ? "Allocating Address..." : "No active Playit tunnel"),
                            PublicAddress = tunnel?.PublicAddress ?? string.Empty,
                            ProtocolLabel = tunnel?.TunnelTypeDisplay ?? p.Name,
                            LocalPortLabel = $"{p.Port} / {p.Protocol}",
                            IsOnline = isOnline,
                            IsLeased = _portLeaseRegistry.GetAllLeases().Any(l => l.Port == p.Port && l.InstanceId == server.Id),
                            HasTunnel = tunnel != null,
                            ActiveColor = activeColor,
                            ServiceRoleName = roleName,
                            ServiceRoleDetail = roleDetail,
                            Port = p.Port,
                            PortType = portType,
                            ServerId = server.Id,
                            Protocol = p.Protocol.ToString().ToUpperInvariant()
                        };

                        serverGroup.Routes.Add(route);
                    }

                    groupsList.Add(serverGroup);
                }

                _dispatcher.Invoke(() =>
                {
                    if (groupsList.Count == 0)
                    {
                        NoRoutesPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        RoutesList.ItemsSource = groupsList;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh interactive ports map.");
                _dispatcher.Invoke(() =>
                {
                    NoRoutesPanel.Visibility = Visibility.Visible;
                });
            }
            finally
            {
                _dispatcher.Invoke(() =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                });
            }
        }

        public class ServerGroupViewModel
        {
            public Guid ServerId { get; set; }
            public string ServerName { get; set; } = string.Empty;
            public string ServerInfo { get; set; } = string.Empty;
            public string PlayerMetrics { get; set; } = string.Empty;
            public Brush ActiveColor { get; set; } = OfflineGreyBrush;
            public bool IsOnline { get; set; }
            public bool IsExpanded { get; set; }
            public string ActionButtonText { get; set; } = "View Console";
            public Wpf.Ui.Controls.SymbolRegular ActionButtonIcon { get; set; } = Wpf.Ui.Controls.SymbolRegular.Code24;
            public List<RouteViewModel> Routes { get; set; } = new();

            public Visibility PlayerMetricsVisibility => string.IsNullOrEmpty(PlayerMetrics) ? Visibility.Collapsed : Visibility.Visible;
        }

        public class RouteViewModel : ViewModelBase
        {
            public string PublicAddressLabel { get; set; } = "Allocating Address...";
            public string PublicAddress { get; set; } = string.Empty;
            public string ProtocolLabel { get; set; } = "TCP";
            public string LocalPortLabel { get; set; } = "25565";
            public bool IsOnline { get; set; }
            public bool IsLeased { get; set; }
            public bool HasTunnel { get; set; }
            public Brush ActiveColor { get; set; } = OfflineGreyBrush;
            public string ServiceRoleName { get; set; } = "Minecraft Java Port";
            public string ServiceRoleDetail { get; set; } = "Standard Java server endpoint";

            public int Port { get; set; }
            public string PortType { get; set; } = string.Empty;
            public Guid ServerId { get; set; }
            public string Protocol { get; set; } = "TCP";

            private bool _isEditingPort;
            public bool IsEditingPort
            {
                get => _isEditingPort;
                set => SetProperty(ref _isEditingPort, value);
            }

            private string _editingPortText = string.Empty;
            public string EditingPortText
            {
                get => _editingPortText;
                set => SetProperty(ref _editingPortText, value);
            }
        }
    }
}
