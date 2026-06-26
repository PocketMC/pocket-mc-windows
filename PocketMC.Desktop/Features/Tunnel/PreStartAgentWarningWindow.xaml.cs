using System;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PreStartAgentWarningWindow : FluentWindow
    {
        private readonly AgentProvisioningService _provisioningService;
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        public PreStartAgentWarningWindow(AgentProvisioningService provisioningService)
        {
            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);
            _provisioningService = provisioningService;

            _provisioningService.StateChanged += OnStateChanged;
            Closed += (s, e) =>
            {
                _provisioningService.StateChanged -= OnStateChanged;
                _tcs.TrySetResult(false);
            };
        }

        private void OnStateChanged(object? sender, AgentConnectionState e)
        {
            if (e == AgentConnectionState.Connected)
            {
                Dispatcher.Invoke(() =>
                {
                    _tcs.TrySetResult(true);
                    Close();
                });
            }
        }

        public Task<bool> WaitForResultAsync()
        {
            return _tcs.Task;
        }

        private async void ConnectAgentButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectAgentButton.IsEnabled = false;
            await _provisioningService.ConnectAsync();

            // Close the dialog and abort the start flow so the user can interact with the Setup Wizard freely
            _tcs.TrySetResult(false);
            Close();
        }

        private void StartAnywayButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(true);
            Close();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}
