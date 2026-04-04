using System;
using System.Diagnostics;
using System.Windows;

namespace PocketMC.Desktop.Views
{
    /// <summary>
    /// Code-behind for PlayitGuideWindow.
    /// Handles browser opening, auto-close on agent connection,
    /// and manual close fallback.
    /// </summary>
    public partial class PlayitGuideWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly Services.PlayitAgentService _agentService;

        public PlayitGuideWindow(Services.PlayitAgentService agentService, string claimUrl)
        {
            InitializeComponent();

            _agentService = agentService;

            // Open the claim URL in the user's default browser (NET-03)
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = claimUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not open browser: {ex.Message}";
            }

            // Subscribe to the tunnel-running event to auto-close (NET-04)
            _agentService.OnTunnelRunning += OnTunnelRunning;
        }

        private void OnTunnelRunning(object? sender, EventArgs e)
        {
            // Must dispatch to UI thread
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "✓ Agent connected!";
                _agentService.OnTunnelRunning -= OnTunnelRunning;
                Close();
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _agentService.OnTunnelRunning -= OnTunnelRunning;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _agentService.OnTunnelRunning -= OnTunnelRunning;
            base.OnClosed(e);
        }
    }
}
