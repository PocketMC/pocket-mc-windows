using System;
using System.Windows;
using Microsoft.Win32;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Views;

namespace PocketMC.Desktop
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public static ResourceMonitorService GlobalMonitor { get; } = new ResourceMonitorService();
        private BackupSchedulerService? _backupScheduler;

        public MainWindow()
        {
            InitializeComponent();
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            Closing += MainWindow_Closing;

            GlobalMonitor.OnGlobalMetricsUpdated += UpdateGlobalHealth;
        }

        private void UpdateGlobalHealth()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                double commitedMb = GlobalMonitor.GetTotalCommittedRamMb();
                double totalMb = PocketMC.Desktop.Utils.SystemMetrics.GetTotalPhysicalMemoryMb();
                GlobalHealthTextBlock.Text = $"Global RAM: {Math.Round(commitedMb)} MB / {Math.Round(totalMb)} MB";

                if (totalMb > 0 && commitedMb > totalMb * 0.9)
                    GlobalHealthTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                else
                    GlobalHealthTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
            });
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _backupScheduler?.Dispose();
            GlobalMonitor.Dispose();
            ServerProcessManager.KillAll();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var settingsManager = new SettingsManager();
            var settings = settingsManager.Load();

            if (string.IsNullOrEmpty(settings.AppRootPath))
            {
                var dialog = new OpenFolderDialog()
                {
                    Title = "Select First-Run Root Folder for PocketMC",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    settings.AppRootPath = dialog.FolderName;
                    settingsManager.Save(settings);
                }
                else
                {
                    Application.Current.Shutdown();
                    return;
                }
            }

            // Start the background backup scheduler
            _backupScheduler = new BackupSchedulerService(settings.AppRootPath);
            _backupScheduler.Start();

            RootFrame.Navigate(new JavaSetupPage(settings.AppRootPath));
        }
    }
}