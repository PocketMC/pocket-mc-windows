using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using PocketMC.Desktop.Utils;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Models;
using System.Linq;
using System.Threading.Tasks;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PocketMC.Desktop.Views
{
    /// <summary>
    /// ViewModel wrapper for InstanceMetadata that adds live state tracking.
    /// </summary>
    public class InstanceCardViewModel : INotifyPropertyChanged
    {
        private readonly InstanceMetadata _metadata;
        private ServerState _state = ServerState.Stopped;

        public InstanceCardViewModel(InstanceMetadata metadata)
        {
            _metadata = metadata;

            // Sync initial state from ServerProcessManager
            if (ServerProcessManager.IsRunning(metadata.Id))
            {
                var proc = ServerProcessManager.GetProcess(metadata.Id);
                _state = proc?.State ?? ServerState.Stopped;
            }
        }

        public InstanceMetadata Metadata => _metadata;
        public Guid Id => _metadata.Id;
        public string Name => _metadata.Name;
        public string Description => _metadata.Description;

        public bool IsRunning => _state == ServerState.Starting || _state == ServerState.Online || _state == ServerState.Stopping;
        public bool IsWaitingToRestart => ServerProcessManager.IsWaitingToRestart(Id);
        public bool ShowRunningControls => IsRunning || IsWaitingToRestart;
        public string StopButtonText => IsWaitingToRestart ? "Abort" : "Stop";

        private string? _countdownText;
        public string StatusText => _countdownText ?? _state switch
        {
            ServerState.Stopped => "● Stopped",
            ServerState.Starting => "◉ Starting...",
            ServerState.Online => "● Online",
            ServerState.Stopping => "◉ Stopping...",
            ServerState.Crashed => "✖ Crashed",
            _ => "Unknown"
        };

        public Brush StatusColor => _state switch
        {
            ServerState.Online => Brushes.LimeGreen,
            ServerState.Starting or ServerState.Stopping => Brushes.Orange,
            ServerState.Crashed => Brushes.Red,
            _ => Brushes.Gray
        };

        // LiveCharts properties
        public ObservableCollection<double> CpuHistory { get; } = new();
        public ObservableCollection<double> RamHistory { get; } = new();
        public LiveChartsCore.ISeries[]? CpuSeries { get; set; }
        public LiveChartsCore.ISeries[]? RamSeries { get; set; }
        
        public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint InvisiblePaint { get; set; } = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Transparent);
        
        public LiveChartsCore.SkiaSharpView.Axis[] InvisibleXAxes { get; set; } = new[] 
        { 
            new LiveChartsCore.SkiaSharpView.Axis { IsVisible = false, ShowSeparatorLines = false } 
        };
        public LiveChartsCore.SkiaSharpView.Axis[] InvisibleYAxes { get; set; } = new[] 
        { 
            new LiveChartsCore.SkiaSharpView.Axis { IsVisible = false, MinLimit = 0, ShowSeparatorLines = false } 
        };

        private string _playerStatus = "0 Players Online";
        public string PlayerStatus { get => _playerStatus; set { _playerStatus = value; OnPropertyChanged(nameof(PlayerStatus)); } }

        private string _cpuText = "CPU 0%";
        public string CpuText { get => _cpuText; set { _cpuText = value; OnPropertyChanged(nameof(CpuText)); } }

        private string _ramText = "RAM 0 MB";
        public string RamText { get => _ramText; set { _ramText = value; OnPropertyChanged(nameof(RamText)); } }

        // Tunnel address (NET-06, D-03)
        private string? _tunnelAddress;
        public string? TunnelAddress
        {
            get => _tunnelAddress;
            set
            {
                _tunnelAddress = value;
                OnPropertyChanged(nameof(TunnelAddress));
                OnPropertyChanged(nameof(HasTunnelAddress));
            }
        }
        public bool HasTunnelAddress => !string.IsNullOrEmpty(_tunnelAddress);

        public void EnsureChartsReady()
        {
            if (CpuSeries != null) return; // Only init once
            CpuSeries = new LiveChartsCore.ISeries[]
            {
                new LiveChartsCore.SkiaSharpView.LineSeries<double>
                {
                    Values = CpuHistory,
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#204CAF50")),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#4CAF50")) { StrokeThickness = 2 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0.5
                }
            };

            RamSeries = new LiveChartsCore.ISeries[]
            {
                new LiveChartsCore.SkiaSharpView.LineSeries<double>
                {
                    Values = RamHistory,
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#202196F3")),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#2196F3")) { StrokeThickness = 2 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0.5
                }
            };
            OnPropertyChanged(nameof(CpuSeries));
            OnPropertyChanged(nameof(RamSeries));
        }

        public void UpdateState(ServerState newState)
        {
            _countdownText = null;
            _state = newState;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(IsWaitingToRestart));
            OnPropertyChanged(nameof(StopButtonText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        public void UpdateCountdown(int seconds)
        {
            _countdownText = $"Restarting in {seconds}s...";
            OnPropertyChanged(nameof(IsWaitingToRestart));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(StopButtonText));
            _state = ServerState.Crashed;
            OnPropertyChanged(nameof(StatusText));
        }

        public void RefreshNameDescription()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class DashboardPage : Page
    {
        private readonly InstanceManager _instanceManager;
        private readonly string _appRootPath;
        private ObservableCollection<InstanceCardViewModel> _viewModels = new();

        /// <summary>
        /// App-scoped Playit agent — started once, lives for app lifetime (NET-02).
        /// Static so other services (TunnelService) can reference it.
        /// </summary>
        public static PlayitAgentService? PlayitAgent { get; private set; }

        public DashboardPage(string appRootPath)
        {
            InitializeComponent();
            _appRootPath = appRootPath;
            _instanceManager = new InstanceManager(appRootPath);
            LoadInstances();
            
            _ = CheckJavaRequirementsAsync(); // Async Java check (NET-14)

            // Subscribe to global state changes
            ServerProcessManager.OnInstanceStateChanged += OnServerStateChanged;
            ServerProcessManager.OnRestartCountdownTick += OnRestartCountdownTick;
            MainWindow.GlobalMonitor.OnGlobalMetricsUpdated += UpdateMetrics;

            // Start Playit agent if not already running (NET-02)
            InitializePlayitAgent();
        }

        /// <summary>
        /// Initializes and starts the Playit.gg background agent (NET-02).
        /// Subscribes to claim URL events to show the guide window (NET-03).
        /// </summary>
        private void InitializePlayitAgent()
        {
            if (PlayitAgent != null) return; // Already initialized

            string playitPath = System.IO.Path.Combine(_appRootPath, "tunnel", "playit.exe");
            if (!System.IO.File.Exists(playitPath)) return; // Not downloaded yet

            PlayitAgent = new PlayitAgentService(_appRootPath, new JobObject());

            // When claim URL is detected, show the guide window (NET-03)
            PlayitAgent.OnClaimUrlReceived += (sender, claimUrl) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var guideWindow = new PlayitGuideWindow(PlayitAgent, claimUrl);
                    guideWindow.Owner = Window.GetWindow(this);
                    guideWindow.Show();
                });
            };

            PlayitAgent.Start();
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
                var dictionary = MainWindow.GlobalMonitor.Metrics;
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
                    var apiClient = new PlayitApiClient();
                    var result = await apiClient.GetTunnelsAsync();
                    if (!result.Success) return;

                    foreach (var vm in missingTunnels)
                    {
                        string? folderName = FindFolderById(vm.Id);
                        if (folderName == null) continue;

                        string serverDir = System.IO.Path.Combine(_appRootPath, "servers", folderName);
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
                catch { }
            });
        }

        private void LoadInstances()
        {
            var instances = _instanceManager.GetAllInstances();
            _viewModels = new ObservableCollection<InstanceCardViewModel>(
                instances.Select(m => new InstanceCardViewModel(m)));
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
                NavigationService.Navigate(new ServerSettingsPage(vm.Metadata, _appRootPath));
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
                // 0. Java Version Check before starting (NET-14)
                string javaPath = JavaVersionHelper.GetRecommendedJavaPath(vm.Metadata.MinecraftVersion, _appRootPath, vm.Metadata.CustomJavaPath);
                var javaCheck = await JavaVersionHelper.CheckInstallationAsync(javaPath);
                
                int required = JavaVersionHelper.GetRequiredJavaVersion(vm.Metadata.MinecraftVersion);

                if (javaCheck.IsAvailable && javaCheck.MajorVersion < required)
                {
                    string source = (javaPath == "java") ? "System 'java'" : $"Selected Java ({javaPath})";
                    var res = System.Windows.MessageBox.Show(
                        $"{source} is Version {javaCheck.MajorVersion}, but Minecraft {vm.Metadata.MinecraftVersion} requires Version {required}+.\n\n" +
                        "The server will likely fail to start. Continue anyway?",
                        "Java Compatibility Warning",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    
                    if (res == System.Windows.MessageBoxResult.No) return;
                }

                // Resolve tunnel address before starting (NET-06, NET-09)
                await ResolveTunnelForInstance(vm);

                ServerProcessManager.StartProcess(vm.Metadata, _appRootPath);
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
            if (PlayitAgent == null || 
               (PlayitAgent.State != PlayitAgentState.Connected && PlayitAgent.State != PlayitAgentState.Starting))
            {
                vm.TunnelAddress = null;
                return;
            }

            // Read server port from server.properties
            string? folderName = FindFolderById(vm.Id);
            if (folderName == null) return;

            string serverDir = System.IO.Path.Combine(_appRootPath, "servers", folderName);
            string propsPath = System.IO.Path.Combine(serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsPath);
            int serverPort = 25565; // Default Minecraft port
            if (props.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var parsed))
                serverPort = parsed;

            var apiClient = new PlayitApiClient();
            var tunnelService = new TunnelService(apiClient, PlayitAgent);
            var result = await tunnelService.ResolveTunnelAsync(serverPort);

            switch (result.Status)
            {
                case TunnelResolutionResult.TunnelStatus.Found:
                    vm.TunnelAddress = result.PublicAddress;
                    break;

                case TunnelResolutionResult.TunnelStatus.LimitReached:
                    var tunnelResult = await apiClient.GetTunnelsAsync();
                    var dialog = new TunnelLimitDialog(tunnelResult.Tunnels);
                    dialog.Owner = Window.GetWindow(this);
                    dialog.ShowDialog();
                    vm.TunnelAddress = null;
                    break;

                case TunnelResolutionResult.TunnelStatus.CreationStarted:
                    var guideWindow = new TunnelCreationGuideWindow(tunnelService, serverPort);
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
                vm.TunnelAddress = "Copied!";
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    Dispatcher.Invoke(() => vm.TunnelAddress = originalAddress);
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
                    ServerProcessManager.AbortRestartDelay(vm.Id);
                }
                else
                {
                    await ServerProcessManager.StopProcessAsync(vm.Id);
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

            var process = ServerProcessManager.GetProcess(vm.Id);
            if (process != null)
            {
                NavigationService.Navigate(new ServerConsolePage(vm.Metadata, process));
            }
        }

        private string? FindFolderById(Guid id)
        {
            var settings = new SettingsManager().Load();
            if (string.IsNullOrEmpty(settings.AppRootPath)) return null;

            var dirPath = System.IO.Path.Combine(settings.AppRootPath, "servers");
            if (!System.IO.Directory.Exists(dirPath)) return null;

            foreach (var dir in System.IO.Directory.GetDirectories(dirPath))
            {
                var metaFile = System.IO.Path.Combine(dir, ".pocket-mc.json");
                if (System.IO.File.Exists(metaFile))
                {
                    var content = System.IO.File.ReadAllText(metaFile);
                    if (content.Contains(id.ToString()))
                        return new System.IO.DirectoryInfo(dir).Name;
                }
            }
            return null;
        }

        private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewInstanceDialog(_instanceManager, _appRootPath);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                LoadInstances();
            }
        }

        private void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            if (ServerProcessManager.IsRunning(vm.Id))
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
                    _instanceManager.DeleteInstance(folder);
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
        private async Task CheckJavaRequirementsAsync()
        {
            try
            {
                var check = await JavaVersionHelper.CheckInstallationAsync();
                
                // Requirement (April 2026 Context): Minecraft 1.21+ needs Java 21+
                // Latest Minecraft might need Java 25 (69.0)
                if (!check.IsAvailable || check.MajorVersion < 21)
                {
                    Dispatcher.Invoke(() => 
                    {
                        TxtJavaInfo.Text = check.IsAvailable 
                            ? $"System 'java' is Version {check.MajorVersion}. Modern Minecraft requires Java 21 or newer."
                            : "No Java installation was detected on your system path.";
                            
                        JavaWarningBanner.Visibility = Visibility.Visible;
                    });
                }
            }
            catch { /* Silent fail for background check */ }
        }

        private void OnJavaDownload_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://adoptium.net/temurin/releases/", UseShellExecute = true });
        }
        private void CopyCrashReport_Click(object sender, RoutedEventArgs e)
        {
            var m = sender as MenuItem;
            var vm = m?.DataContext as InstanceCardViewModel;
            if (vm == null) return;

            var process = ServerProcessManager.GetProcess(vm.Id);
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
