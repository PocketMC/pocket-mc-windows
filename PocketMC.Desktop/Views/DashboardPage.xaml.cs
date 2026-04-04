using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using PocketMC.Desktop.Utils;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.ViewModels;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PocketMC.Desktop.Views
{
    public partial class DashboardPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly InstanceManager _instanceManager;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ResourceMonitorService _resourceMonitorService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly SettingsManager _settingsManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DashboardPage> _logger;
        private ObservableCollection<InstanceCardViewModel> _viewModels = new();
        private bool _isSubscribedToServices;
        private int _playitHealthRefreshVersion;

        public DashboardPage(
            ApplicationState applicationState,
            InstanceManager instanceManager,
            ServerProcessManager serverProcessManager,
            ResourceMonitorService resourceMonitorService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            SettingsManager settingsManager,
            IServiceProvider serviceProvider,
            ILogger<DashboardPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _instanceManager = instanceManager;
            _serverProcessManager = serverProcessManager;
            _resourceMonitorService = resourceMonitorService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _settingsManager = settingsManager;
            _serviceProvider = serviceProvider;
            _logger = logger;
            LoadInstances();

            Loaded += DashboardPage_Loaded;
            Unloaded += DashboardPage_Unloaded;
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isSubscribedToServices)
            {
                _serverProcessManager.OnInstanceStateChanged += OnServerStateChanged;
                _serverProcessManager.OnRestartCountdownTick += OnRestartCountdownTick;
                _resourceMonitorService.OnGlobalMetricsUpdated += UpdateMetrics;
                _playitAgentService.OnClaimUrlReceived += OnClaimUrlReceived;
                _playitAgentService.OnStateChanged += OnPlayitAgentStateChanged;
                _playitAgentService.OnTunnelRunning += OnPlayitTunnelRunning;
                _isSubscribedToServices = true;
            }

            InitializePlayitAgent();
            _ = RefreshPlayitHealthAsync(TimeSpan.FromSeconds(1), retryCount: 3);
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isSubscribedToServices)
            {
                return;
            }

            _serverProcessManager.OnInstanceStateChanged -= OnServerStateChanged;
            _serverProcessManager.OnRestartCountdownTick -= OnRestartCountdownTick;
            _resourceMonitorService.OnGlobalMetricsUpdated -= UpdateMetrics;
            _playitAgentService.OnClaimUrlReceived -= OnClaimUrlReceived;
            _playitAgentService.OnStateChanged -= OnPlayitAgentStateChanged;
            _playitAgentService.OnTunnelRunning -= OnPlayitTunnelRunning;
            _isSubscribedToServices = false;
        }

        private void InitializePlayitAgent()
        {
            // 1. Check for binary (D-02)
            if (!_playitAgentService.IsBinaryAvailable)
            {
                ShowPlayitStatusBanner("⚠ Playit.gg agent not found", "Agent binary missing. Please download it to enable tunnels.", isBinaryMissing: true);
                return;
            }

            // 2. Start if not already running (NET-02)
            _playitAgentService.Start();
        }

        private void OnClaimUrlReceived(object? sender, string claimUrl)
        {
            Dispatcher.Invoke(() =>
            {
                ShowPlayitStatusBanner(
                    "Playit.gg setup required",
                    "Approve the agent in your browser to finish linking PocketMC. If you closed the claim page, click Fix to restart the setup flow.");
                var guideWindow = ActivatorUtilities.CreateInstance<PlayitGuideWindow>(_serviceProvider, claimUrl);
                guideWindow.Owner = Window.GetWindow(this);
                guideWindow.Show();
            });
        }

        private void OnPlayitAgentStateChanged(object? sender, PlayitAgentState newState)
        {
            switch (newState)
            {
                case PlayitAgentState.WaitingForClaim:
                    ShowPlayitStatusBanner(
                        "Playit.gg setup required",
                        "Approve the agent in your browser to finish linking PocketMC. If you closed the claim page, click Fix to restart the setup flow.");
                    break;

                case PlayitAgentState.Connected:
                    HidePlayitStatusBanner();
                    _ = RefreshPlayitHealthAsync(TimeSpan.Zero, retryCount: 8);
                    break;

                case PlayitAgentState.Starting:
                    _ = RefreshPlayitHealthAsync(TimeSpan.FromSeconds(1), retryCount: 3);
                    break;
            }
        }

        private void OnPlayitTunnelRunning(object? sender, EventArgs e)
        {
            HidePlayitStatusBanner();
            _ = RefreshPlayitHealthAsync(TimeSpan.Zero, retryCount: 8);
        }

        private async Task RefreshPlayitHealthAsync(TimeSpan initialDelay, int retryCount = 1, TimeSpan? retryDelay = null)
        {
            int refreshVersion = Interlocked.Increment(ref _playitHealthRefreshVersion);
            TimeSpan effectiveRetryDelay = retryDelay ?? TimeSpan.FromSeconds(2);

            if (initialDelay > TimeSpan.Zero)
            {
                await Task.Delay(initialDelay);
            }

            for (int attempt = 1; attempt <= Math.Max(1, retryCount); attempt++)
            {
                try
                {
                    var result = await _playitApiClient.GetTunnelsAsync();
                    if (refreshVersion != Volatile.Read(ref _playitHealthRefreshVersion))
                    {
                        return;
                    }

                    if (result.Success)
                    {
                        HidePlayitStatusBanner(refreshVersion);
                        return;
                    }

                    if (result.RequiresClaim)
                    {
                        if (_playitAgentService.State == PlayitAgentState.Connected && attempt < retryCount)
                        {
                            await Task.Delay(effectiveRetryDelay);
                            continue;
                        }

                        ShowPlayitStatusBanner(
                            "Playit.gg setup required",
                            "PocketMC is still waiting for Playit account approval. Finish the claim in your browser, or click Fix to restart the setup flow.",
                            refreshVersion: refreshVersion);
                        return;
                    }

                    if (result.IsTokenInvalid)
                    {
                        ShowPlayitStatusBanner(
                            "⚠ Playit.gg: token invalid",
                            "The saved Playit credentials were rejected. Click Fix to reset the local token and re-link your account.",
                            isTokenInvalid: true,
                            refreshVersion: refreshVersion);
                        return;
                    }

                    if (_playitAgentService.State == PlayitAgentState.Connected && attempt < retryCount)
                    {
                        await Task.Delay(effectiveRetryDelay);
                        continue;
                    }

                    // Non-auth API failures should not leave a stale invalid banner on screen.
                    HidePlayitStatusBanner(refreshVersion);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check Playit health on attempt {Attempt}.", attempt);

                    if (refreshVersion != Volatile.Read(ref _playitHealthRefreshVersion))
                    {
                        return;
                    }

                    if (attempt < retryCount)
                    {
                        await Task.Delay(effectiveRetryDelay);
                        continue;
                    }

                    if (_playitAgentService.State == PlayitAgentState.Connected)
                    {
                        HidePlayitStatusBanner(refreshVersion);
                    }
                }
            }
        }

        private void ShowPlayitStatusBanner(string title, string detail, bool isTokenInvalid = false, bool isBinaryMissing = false, int? refreshVersion = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (refreshVersion.HasValue && refreshVersion.Value != Volatile.Read(ref _playitHealthRefreshVersion))
                {
                    return;
                }

                TxtPlayitStatus.Inlines.Clear();
                TxtPlayitStatus.Inlines.Add(new System.Windows.Documents.Run(title) { FontWeight = FontWeights.Bold });
                TxtPlayitDetail.Text = detail;
                PlayitStatusBanner.Visibility = Visibility.Visible;
                
                // Tag the button so we know what to do
                BtnFixPlayit.Tag = isBinaryMissing ? "DOWNLOAD" : "RESET";
            });
        }

        private void HidePlayitStatusBanner(int? refreshVersion = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (refreshVersion.HasValue && refreshVersion.Value != Volatile.Read(ref _playitHealthRefreshVersion))
                {
                    return;
                }

                PlayitStatusBanner.Visibility = Visibility.Collapsed;
            });
        }

        private async void BtnFixPlayit_Click(object sender, RoutedEventArgs e)
        {
            if (BtnFixPlayit.Tag?.ToString() == "DOWNLOAD")
            {
                // Open playit.gg download or guide flow
                Process.Start(new ProcessStartInfo { FileName = "https://playit.gg/download", UseShellExecute = true });
            }
            else
            {
                // Reset token flow (D-02)
                string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
                
                try
                {
                    await FileUtils.DeleteFileAsync(tomlPath);
                    
                    // Restart agent to get a new claim URL
                    _playitAgentService.Stop();
                    // Wait a bit and restart
                    await Task.Delay(500);
                    _playitAgentService.Start();
                    
                    HidePlayitStatusBanner();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not reset the Playit config at {TomlPath}.", tomlPath);
                    System.Windows.MessageBox.Show("Could not reset Playit config: " + ex.Message);
                }
            }
        }

        private int _metricTicks = 0;

        private void UpdateMetrics()
        {
            _metricTicks++;
            if (_metricTicks % 5 == 0) // Roughly every 10 seconds based on global monitor ticks
            {
                RunBackgroundTunnelPoll();
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dictionary = _resourceMonitorService.Metrics;
                foreach (var vm in _viewModels)
                {
                    if (dictionary.TryGetValue(vm.Id, out var metric))
                    {
                        vm.EnsureChartsReady();
                        
                        vm.CpuHistory.Add(metric.CpuUsage);
                        if (vm.CpuHistory.Count > 30) vm.CpuHistory.RemoveAt(0);
                        vm.CpuText = $"CPU {Math.Round(metric.CpuUsage)}%";

                        vm.RamHistory.Add(metric.RamUsageMb);
                        if (vm.RamHistory.Count > 30) vm.RamHistory.RemoveAt(0);
                        vm.RamText = $"RAM {Math.Round(metric.RamUsageMb)} MB";

                        vm.PlayerStatus = $"{metric.PlayerCount} Players Online";
                        
                        // High RAM warning badge logic
                        if (vm.Metadata.MaxRamMb > 0 && metric.RamUsageMb > vm.Metadata.MaxRamMb * 1.1)
                        {
                            vm.RamText += " ⚠ (High)";
                        }
                    }
                    else
                    {
                        // Default out
                        vm.PlayerStatus = "0 Players Online";
                        vm.CpuText = "CPU 0%";
                        vm.RamText = "RAM 0 MB";
                    }
                }
            });
        }

        private void RunBackgroundTunnelPoll()
        {
            // Only search for running instances missing a tunnel IP
            var missingTunnels = _viewModels.Where(v => v.IsRunning && string.IsNullOrEmpty(v.TunnelAddress)).ToList();
            if (!missingTunnels.Any()) return;

            Task.Run(async () =>
            {
                try
                {
                    var result = await _playitApiClient.GetTunnelsAsync();
                    if (!result.Success) return;

                    HidePlayitStatusBanner();

                    foreach (var vm in missingTunnels)
                    {
                        var serverDir = _instanceManager.GetInstancePath(vm.Id);
                        if (serverDir == null) continue;
                        string propsPath = System.IO.Path.Combine(serverDir, "server.properties");
                        var props = ServerPropertiesParser.Read(propsPath);
                        int serverPort = 25565;
                        if (props.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var parsed))
                            serverPort = parsed;

                        var match = PlayitApiClient.FindTunnelForPort(result.Tunnels, serverPort);
                        if (match != null)
                        {
                            Dispatcher.Invoke(() => vm.TunnelAddress = match.PublicAddress);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Background Playit tunnel polling failed.");
                }
            });
        }

        private void LoadInstances()
        {
            var instances = _instanceManager.GetAllInstances();
            _viewModels = new ObservableCollection<InstanceCardViewModel>(
                instances.Select(m => new InstanceCardViewModel(m, _serverProcessManager)));
            InstanceGrid.ItemsSource = _viewModels;
            TxtEmpty.Visibility = _viewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnServerStateChanged(Guid instanceId, ServerState newState)
        {
            Dispatcher.Invoke(() =>
            {
                var vm = _viewModels.FirstOrDefault(v => v.Id == instanceId);
                vm?.UpdateState(newState);
            });
        }

        private void OnRestartCountdownTick(Guid instanceId, int seconds)
        {
            Dispatcher.Invoke(() =>
            {
                var vm = _viewModels.FirstOrDefault(v => v.Id == instanceId);
                if (vm != null)
                {
                    vm.UpdateCountdown(seconds);
                }
            });
        }

        // --- Helper to get ViewModel from sender ---
        private InstanceCardViewModel? GetViewModel(object sender)
        {
            if (sender is FrameworkElement element && element.DataContext is InstanceCardViewModel vm)
                return vm;
            return null;
        }

        private void BtnMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.DataContext = btn.DataContext;
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm != null)
                NavigationService.Navigate(ActivatorUtilities.CreateInstance<ServerSettingsPage>(_serviceProvider, vm.Metadata));
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm != null)
            {
                string? folderName = FindFolderById(vm.Id);
                if (folderName != null)
                    _instanceManager.OpenInExplorer(folderName);
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                // Resolve tunnel address before starting (NET-06, NET-09)
                await ResolveTunnelForInstance(vm);

                _serverProcessManager.StartProcess(vm.Metadata, _applicationState.GetRequiredAppRootPath());
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "Cannot Start Server",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Resolves the Playit tunnel address for a server instance (NET-06).
        /// Reads server-port from server.properties to match against API tunnels.
        /// </summary>
        private async Task ResolveTunnelForInstance(InstanceCardViewModel vm)
        {
            if (_playitAgentService.State != PlayitAgentState.Connected &&
                _playitAgentService.State != PlayitAgentState.Starting)
            {
                vm.TunnelAddress = null;
                return;
            }

            // Read server port from server.properties
            string? serverDir = _instanceManager.GetInstancePath(vm.Id);
            if (serverDir == null) return;

            string propsPath = System.IO.Path.Combine(serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsPath);
            int serverPort = 25565; // Default Minecraft port
            if (props.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var parsed))
                serverPort = parsed;

            var tunnelService = ActivatorUtilities.CreateInstance<TunnelService>(_serviceProvider);
            var result = await tunnelService.ResolveTunnelAsync(serverPort);

            switch (result.Status)
            {
                case TunnelResolutionResult.TunnelStatus.Found:
                    vm.TunnelAddress = result.PublicAddress;
                    break;

                case TunnelResolutionResult.TunnelStatus.LimitReached:
                    var tunnelResult = await _playitApiClient.GetTunnelsAsync();
                    var dialog = new TunnelLimitDialog(tunnelResult.Tunnels);
                    dialog.Owner = Window.GetWindow(this);
                    dialog.ShowDialog();
                    vm.TunnelAddress = null;
                    break;

                case TunnelResolutionResult.TunnelStatus.CreationStarted:
                    var guideWindow = ActivatorUtilities.CreateInstance<TunnelCreationGuideWindow>(_serviceProvider, serverPort);
                    guideWindow.Owner = Window.GetWindow(this);
                    guideWindow.OnTunnelResolved += (address) => 
                    {
                        Dispatcher.Invoke(() => vm.TunnelAddress = address);
                    };
                    guideWindow.Show();
                    break;

                case TunnelResolutionResult.TunnelStatus.Error:
                    vm.TunnelAddress = null;
                    break;

                default:
                    vm.TunnelAddress = null;
                    break;
            }
        }

        /// <summary>
        /// Click-to-copy handler for tunnel address pill (D-03).
        /// </summary>
        private void TunnelPill_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is InstanceCardViewModel vm
                && !string.IsNullOrEmpty(vm.TunnelAddress))
            {
                System.Windows.Clipboard.SetText(vm.TunnelAddress);
                // Brief visual feedback — swap text momentarily
                var originalAddress = vm.TunnelAddress;
                vm.TunnelAddress = "\u2713 Copied";
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    Dispatcher.Invoke(() =>
                    {
                        if (vm.TunnelAddress == "\u2713 Copied")
                        {
                            vm.TunnelAddress = originalAddress;
                        }
                    });
                });
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                if (vm.IsWaitingToRestart)
                {
                    _serverProcessManager.AbortRestartDelay(vm.Id);
                }
                else
                {
                    await _serverProcessManager.StopProcessAsync(vm.Id);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "Stop Error",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnConsole_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            var process = _serverProcessManager.GetProcess(vm.Id);
            if (process != null)
            {
                NavigationService.Navigate(ActivatorUtilities.CreateInstance<ServerConsolePage>(_serviceProvider, vm.Metadata, process));
            }
        }

        private string? FindFolderById(Guid id)
        {
            var instancePath = _instanceManager.GetInstancePath(id);
            return instancePath == null ? null : System.IO.Path.GetFileName(instancePath);
        }

        private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = _serviceProvider.GetRequiredService<NewInstanceDialog>();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                LoadInstances();
            }
        }

        private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            if (_serverProcessManager.IsRunning(vm.Id))
            {
                System.Windows.MessageBox.Show(
                    "Cannot delete a running server. Stop it first.",
                    "Server Running",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var prompt = System.Windows.MessageBox.Show(
                "Are you sure you want to completely erase the " + vm.Name + " server? All worlds and files will be permanently deleted.",
                "Delete Server",
                MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (prompt == MessageBoxResult.Yes)
            {
                var folder = FindFolderById(vm.Id);
                if (folder != null)
                {
                    await Task.Run(() => _instanceManager.DeleteInstance(folder));
                    LoadInstances();
                }
            }
        }

        private async void RenameInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Rename Server",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel"
            };

            var stackPanel = new StackPanel();
            var txtName = new Wpf.Ui.Controls.TextBox { Text = vm.Name };
            var txtDesc = new Wpf.Ui.Controls.TextBox { Text = vm.Description, Margin = new Thickness(0, 10, 0, 0) };
            stackPanel.Children.Add(txtName);
            stackPanel.Children.Add(txtDesc);
            dialog.Content = stackPanel;

            var result = await dialog.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                if (!string.IsNullOrWhiteSpace(txtName.Text))
                {
                    var folder = FindFolderById(vm.Id);
                    if (folder != null)
                    {
                        _instanceManager.UpdateMetadata(folder, txtName.Text, txtDesc.Text);
                        LoadInstances();
                    }
                }
            }
        }

        private void CopyCrashReport_Click(object sender, RoutedEventArgs e)
        {
            var m = sender as MenuItem;
            var vm = m?.DataContext as InstanceCardViewModel;
            if (vm == null) return;

            var process = _serverProcessManager.GetProcess(vm.Id);
            if (process != null && !string.IsNullOrEmpty(process.CrashContext))
            {
                System.Windows.Clipboard.SetText(process.CrashContext);
                System.Windows.MessageBox.Show("Crash report copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("No crash report available for this instance.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
