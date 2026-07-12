using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class AddonInstallDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<AddonInstallRowViewModel> _items = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        /// <summary>Callback invoked for each item to perform the actual download+install.</summary>
        public Func<AddonInstallRowViewModel, IProgress<DownloadProgress>, CancellationToken, Task>? InstallAction { get; set; }

        public Action? OnAllInstallsCompleted { get; set; }

        public bool AnyInstalled { get; private set; }
        public int InstalledCount { get; private set; }
        public int FailedCount { get; private set; }

        public AddonInstallDialogWindow()
        {
            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);
            ItemsList.ItemsSource = _items;
        }

        public void SetItems(IEnumerable<AddonInstallRowViewModel> items)
        {
            _items.Clear();
            foreach (var item in items)
            {
                _items.Add(item);
            }
        }

        private async void FluentWindow_ContentRendered(object? sender, EventArgs e)
        {
            await Task.Delay(300); // Give UI time to fully render

            if (_items.Count == 0 || InstallAction == null)
            {
                Close();
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            BtnCancel.Content = "Cancel";

            int total = _items.Count;
            int current = 0;
            int succeeded = 0;
            int failed = 0;

            foreach (var item in _items)
            {
                if (_cts.Token.IsCancellationRequested) break;

                current++;
                TxtOverallStatus.Text = $"Installing {current}/{total}: {item.ProjectTitle}...";
                OverallProgressBar.Value = (double)(current - 1) / total * 100;

                item.StatusText = "Downloading...";
                item.StatusForeground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)); // Blue
                item.IsDownloading = true;
                item.ProgressValue = 0;

                var progress = new Progress<DownloadProgress>(dp =>
                {
                    item.ProgressValue = dp.Percentage;
                    if (dp.TotalBytes > 0)
                    {
                        string downloaded = FormatBytes(dp.BytesRead);
                        string totalStr = FormatBytes(dp.TotalBytes);
                        item.StatusText = $"Downloading... {downloaded} / {totalStr}";
                    }
                });

                try
                {
                    await Task.Run(async () => await InstallAction(item, progress, _cts.Token));
                    item.ProgressValue = 100;
                    item.IsDownloading = false;
                    item.StatusText = "\u2713 Installed successfully";
                    item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)); // Green
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    item.IsDownloading = false;
                    item.StatusText = "Cancelled";
                    item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)); // Yellow
                    break;
                }
                catch (Exception ex)
                {
                    item.IsDownloading = false;
                    item.StatusText = $"\u2717 Failed: {ex.Message}";
                    item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)); // Red
                    failed++;
                }
            }

            InstalledCount = succeeded;
            FailedCount = failed;
            AnyInstalled = succeeded > 0;

            OverallProgressBar.Value = 100;

            if (_cts.Token.IsCancellationRequested)
            {
                TxtOverallStatus.Text = $"Cancelled. {succeeded} installed, {total - succeeded - failed} skipped.";
            }
            else if (failed > 0)
            {
                TxtOverallStatus.Text = $"Done. {succeeded} installed, {failed} failed.";
            }
            else
            {
                TxtOverallStatus.Text = $"All {succeeded} addon(s) installed successfully!";
                TxtOverallStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            }

            BtnCancel.Content = "Close";
            _isRunning = false;

            OnAllInstallsCompleted?.Invoke();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
            }
            else
            {
                Close();
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                BtnCancel_Click(BtnCancel, new RoutedEventArgs());
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Dispose();
            base.OnClosed(e);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
