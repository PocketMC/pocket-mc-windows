using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Infrastructure
{
    /// <summary>
    /// Row ViewModel for each updatable addon in the update dialog.
    /// </summary>
    public class AddonUpdateRowViewModel : ViewModelBase
    {
        private bool _isSelected = true;
        private bool _canSelect = true;
        private double _progressValue;
        private bool _isDownloading;
        private string _statusText = "";
        private Brush _statusForeground = Brushes.Gray;

        public string DisplayName { get; init; } = "";
        public string InstalledVersion { get; init; } = "unknown";
        public string LatestVersion { get; init; } = "new version";
        public AddonUpdateInfo UpdateInfo { get; init; } = null!;
        public string OldFileName { get; init; } = "";
        public string Provider { get; init; } = "";
        public string ProjectId { get; init; } = "";

        /// <summary>Tag for the original VM (ModItemViewModel or PluginItemViewModel).</summary>
        public object OriginalVM { get; init; } = null!;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool CanSelect
        {
            get => _canSelect;
            set => SetProperty(ref _canSelect, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set { SetProperty(ref _isDownloading, value); OnPropertyChanged(nameof(HasStatus)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { SetProperty(ref _statusText, value); OnPropertyChanged(nameof(HasStatus)); }
        }

        public Brush StatusForeground
        {
            get => _statusForeground;
            set => SetProperty(ref _statusForeground, value);
        }

        public bool HasStatus => !string.IsNullOrEmpty(StatusText) || IsDownloading;
    }

    /// <summary>
    /// A styled FluentWindow that shows a checklist of available addon updates
    /// and per-item download progress bars when updates are being installed.
    /// </summary>
    public partial class AddonUpdateDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<AddonUpdateRowViewModel> _items = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        /// <summary>Callback invoked for each selected addon to perform the actual download+install.</summary>
        public Func<AddonUpdateRowViewModel, IProgress<DownloadProgress>, CancellationToken, Task>? InstallAction { get; set; }

        /// <summary>Called after all updates complete (success or partial).</summary>
        public Action? OnAllUpdatesCompleted { get; set; }

        /// <summary>True if at least one update was installed successfully.</summary>
        public bool AnyInstalled { get; private set; }

        public int InstalledCount { get; private set; }
        public int FailedCount { get; private set; }

        public AddonUpdateDialogWindow()
        {
            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);
            ItemsList.ItemsSource = _items;
        }

        /// <summary>
        /// Populates the dialog with a list of updatable addons.
        /// </summary>
        public void SetItems(IEnumerable<AddonUpdateRowViewModel> items)
        {
            _items.Clear();
            foreach (var item in items)
            {
                _items.Add(item);
            }

            TxtSummary.Text = $"{_items.Count} addon(s) have updates available. Select the ones you want to update.";
        }

        private void ChkSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            bool check = ChkSelectAll.IsChecked == true;
            foreach (var item in _items)
            {
                if (item.CanSelect)
                    item.IsSelected = check;
            }
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || InstallAction == null) return;

            var selected = _items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                AppDialog.ShowInfo("No Selection", "Please select at least one addon to update.");
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            BtnUpdate.IsEnabled = false;
            BtnUpdate.Content = "Updating...";
            BtnCancel.Content = "Cancel";
            ChkSelectAll.IsEnabled = false;

            // Lock all checkboxes
            foreach (var item in _items)
            {
                item.CanSelect = false;
            }

            // Show overall progress
            TxtOverallStatus.Visibility = Visibility.Visible;
            OverallProgressBar.Visibility = Visibility.Visible;

            int total = selected.Count;
            int current = 0;
            int succeeded = 0;
            int failed = 0;

            foreach (var item in selected)
            {
                if (_cts.Token.IsCancellationRequested) break;

                current++;
                TxtOverallStatus.Text = $"Updating {current}/{total}: {item.DisplayName}...";
                OverallProgressBar.Value = (double)(current - 1) / total * 100;

                item.StatusText = "Downloading...";
                item.StatusForeground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)); // Blue
                item.IsDownloading = true;
                item.ProgressValue = 0;

                var progress = new Progress<DownloadProgress>(dp =>
                {
                    // Update on UI thread via Progress<T>
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
                    await InstallAction(item, progress, _cts.Token);
                    item.ProgressValue = 100;
                    item.IsDownloading = false;
                    item.StatusText = "âœ“ Updated successfully";
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
                    item.StatusText = $"âœ• Failed: {ex.Message}";
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
                TxtOverallStatus.Text = $"Cancelled. {succeeded} updated, {total - succeeded - failed} skipped.";
            }
            else if (failed > 0)
            {
                TxtOverallStatus.Text = $"Done. {succeeded} updated, {failed} failed.";
            }
            else
            {
                TxtOverallStatus.Text = $"All {succeeded} addon(s) updated successfully!";
                TxtOverallStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            }

            BtnUpdate.Visibility = Visibility.Collapsed;
            BtnCancel.Content = "Close";
            _isRunning = false;

            OnAllUpdatesCompleted?.Invoke();
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
