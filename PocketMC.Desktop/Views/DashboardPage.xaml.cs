using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;
using System.Linq;
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
        public LiveChartsCore.ISeries[] CpuSeries { get; set; }
        public LiveChartsCore.ISeries[] RamSeries { get; set; }
        
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

        public DashboardPage(string appRootPath)
        {
            InitializeComponent();
            _appRootPath = appRootPath;
            _instanceManager = new InstanceManager(appRootPath);
            LoadInstances();

            // Subscribe to global state changes
            ServerProcessManager.OnInstanceStateChanged += OnServerStateChanged;
            ServerProcessManager.OnRestartCountdownTick += OnRestartCountdownTick;
            MainWindow.GlobalMonitor.OnGlobalMetricsUpdated += UpdateMetrics;
        }

        private void UpdateMetrics()
        {
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

        private void LoadInstances()
        {
            var instances = _instanceManager.GetAllInstances();
            _viewModels = new ObservableCollection<InstanceCardViewModel>(
                instances.Select(m => new InstanceCardViewModel(m)));
            InstanceGrid.ItemsSource = _viewModels;
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

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
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
    }
}
