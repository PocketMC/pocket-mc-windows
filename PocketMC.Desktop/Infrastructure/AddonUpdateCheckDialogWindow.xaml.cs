using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Infrastructure
{
    public class AddonUpdateCheckRowViewModel : ViewModelBase
    {
        private bool _isChecking;
        private string _statusText = "Waiting...";
        private Brush _statusForeground = Brushes.Gray;

        public string DisplayName { get; init; } = "";
        public object OriginalVM { get; init; } = null!;
        public Func<Task<bool>> CheckAction { get; init; } = null!;

        public bool IsChecking
        {
            get => _isChecking;
            set => SetProperty(ref _isChecking, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public Brush StatusForeground
        {
            get => _statusForeground;
            set => SetProperty(ref _statusForeground, value);
        }

        public bool HasUpdate { get; set; }
    }

    public partial class AddonUpdateCheckDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<AddonUpdateCheckRowViewModel> _items = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public bool UpdatesFound { get; private set; }
        public bool ProceedToUpdate { get; private set; }
        public bool IsCancelled { get; private set; }

        public AddonUpdateCheckDialogWindow()
        {
            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);
            ItemsList.ItemsSource = _items;
        }

        public void SetItems(IEnumerable<AddonUpdateCheckRowViewModel> items)
        {
            _items.Clear();
            foreach (var item in items)
            {
                _items.Add(item);
            }
            TxtSummary.Text = $"Checking {_items.Count} addon(s) for updates...";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;

            _isRunning = true;
            _cts = new CancellationTokenSource();
            BtnCancel.Content = "Cancel";
            BtnProceed.Visibility = Visibility.Collapsed;

            TxtOverallStatus.Visibility = Visibility.Visible;
            OverallProgressBar.Visibility = Visibility.Visible;

            int total = _items.Count;
            int current = 0;
            int updatable = 0;
            int upToDate = 0;
            int failed = 0;

            foreach (var item in _items)
            {
                if (_cts.Token.IsCancellationRequested) break;

                current++;
                TxtOverallStatus.Text = $"Checking {current}/{total}: {item.DisplayName}...";
                OverallProgressBar.Value = (double)(current - 1) / total * 100;

                item.IsChecking = true;
                item.StatusText = "Checking...";
                item.StatusForeground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)); // Blue

                try
                {
                    bool hasUpdate = await item.CheckAction();
                    item.IsChecking = false;
                    item.HasUpdate = hasUpdate;

                    if (hasUpdate)
                    {
                        item.StatusText = "Update available";
                        item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)); // Green
                        updatable++;
                    }
                    else
                    {
                        item.StatusText = "Up to date";
                        item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)); // Subtext
                        upToDate++;
                    }
                }
                catch (OperationCanceledException)
                {
                    item.IsChecking = false;
                    item.StatusText = "Cancelled";
                    item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)); // Yellow
                    break;
                }
                catch (Exception ex)
                {
                    item.IsChecking = false;
                    item.StatusText = $"Failed: {ex.Message}";
                    item.StatusForeground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)); // Red
                    failed++;
                }
            }

            OverallProgressBar.Value = 100;
            UpdatesFound = updatable > 0;

            if (_cts.Token.IsCancellationRequested)
            {
                TxtOverallStatus.Text = "Check cancelled.";
                BtnCancel.Content = "Close";
                IsCancelled = true;
                _isRunning = false;
            }
            else
            {
                if (UpdatesFound)
                {
                    TxtOverallStatus.Text = $"Done. {updatable} update(s) found.";
                    TxtHeaderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowUp24;
                    TxtTitle.Text = "Updates Available";
                    BtnProceed.Visibility = Visibility.Visible;
                    
                    // Auto-proceed after a tiny delay for visual completion
                    await Task.Delay(500);
                    ProceedToUpdate = true;
                    Close();
                }
                else
                {
                    TxtOverallStatus.Text = $"Done. All addons are up to date. ({failed} failed)";
                    TxtHeaderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark24;
                    TxtTitle.Text = "Up to Date";
                    TxtHeaderIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                    TxtTitle.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                    
                    // Auto-close after a tiny delay
                    await Task.Delay(500);
                    ProceedToUpdate = false;
                    Close();
                }
            }
        }

        private void BtnProceed_Click(object sender, RoutedEventArgs e)
        {
            ProceedToUpdate = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
            }
            else
            {
                ProceedToUpdate = false;
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
    }
}
