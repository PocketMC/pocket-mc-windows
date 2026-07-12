using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Infrastructure
{
    public partial class ProgressDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly Func<IProgress<double>, Task> _action;
        private bool _isCompleted = false;

        public ProgressDialogWindow(string title, string message, Func<IProgress<double>, Task> action)
        {
            InitializeComponent();
            _action = action;

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
            var progress = new Progress<double>(value =>
            {
                DialogProgressBar.Value = value;
            });

            try
            {
                await _action(progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during transfer:\n{ex.Message}", "Transfer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isCompleted = true;
                Close();
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
