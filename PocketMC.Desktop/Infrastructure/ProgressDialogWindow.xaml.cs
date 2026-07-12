using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Infrastructure
{
    public struct ProgressDialogUpdate
    {
        public double Percentage { get; set; }
        public string? Message { get; set; }
    }

    public partial class ProgressDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> _action;
        private readonly CancellationTokenSource? _cts;
        private bool _isCompleted = false;

        public ProgressDialogWindow(string title, string message, Func<IProgress<double>, Task> action)
        {
            InitializeComponent();
            _action = (prog, ct) => action(new Progress<double>(val => prog.Report(new ProgressDialogUpdate { Percentage = val })));
            Init(title, message);
        }

        public ProgressDialogWindow(string title, string message, Func<IProgress<double>, CancellationToken, Task> action, CancellationTokenSource cts)
        {
            InitializeComponent();
            _action = (prog, ct) => action(new Progress<double>(val => prog.Report(new ProgressDialogUpdate { Percentage = val })), ct);
            _cts = cts;
            Init(title, message);
            BtnCancel.Visibility = Visibility.Visible;
        }

        public ProgressDialogWindow(string title, string message, Func<IProgress<ProgressDialogUpdate>, CancellationToken, Task> action, CancellationTokenSource cts)
        {
            InitializeComponent();
            _action = action;
            _cts = cts;
            Init(title, message);
            BtnCancel.Visibility = Visibility.Visible;
        }

        private void Init(string title, string message)
        {
            Title = title;
            MessageText.Text = message;

            // Apply DWM Dark mode fix and accent colors
            var visualService = ServiceProviderServiceExtensions.GetRequiredService<IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            try
            {
                if (System.Windows.Application.Current is App app)
                {
                    var accentService = ServiceProviderServiceExtensions.GetService<AccentColorService>(app.Services);
                    accentService?.ReassertAccent();
                }
            }
            catch
            {
                // Non-critical
            }

            Loaded += ProgressDialogWindow_Loaded;
            Closing += ProgressDialogWindow_Closing;
        }

        private async void ProgressDialogWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            var progress = new Progress<ProgressDialogUpdate>(update =>
            {
                if (update.Percentage < 0)
                {
                    DialogProgressBar.IsIndeterminate = true;
                }
                else
                {
                    DialogProgressBar.IsIndeterminate = false;
                    DialogProgressBar.Value = update.Percentage;
                }

                if (!string.IsNullOrEmpty(update.Message))
                {
                    DetailText.Text = update.Message;
                }
            });

            try
            {
                await _action(progress, _cts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Clean exit on cancellation
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during operation:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isCompleted = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_isCompleted && _cts != null)
            {
                _cts.Cancel();
                BtnCancel.IsEnabled = false;
                BtnCancel.Content = "Cancelling...";
            }
        }

        private void ProgressDialogWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Prevent the user from closing the dialog using Alt+F4 or other means before completion
            if (!_isCompleted)
            {
                e.Cancel = true;
            }
        }
    }
}
