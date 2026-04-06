using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace PocketMC.Desktop.Views
{
    public partial class PlayitGuidePage : Page
    {
        private readonly Services.PlayitAgentService _agentService;

        public PlayitGuidePage(Services.PlayitAgentService agentService, string claimUrl)
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

            Unloaded += PlayitGuidePage_Unloaded;
        }

        private void OnTunnelRunning(object? sender, EventArgs e)
        {
            // Must dispatch to UI thread
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "✓ Agent connected!";
                _agentService.OnTunnelRunning -= OnTunnelRunning;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                if (mainWindow?.NavigateBackFromDetail() == true) return;
                if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
            });
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            _agentService.OnTunnelRunning -= OnTunnelRunning;
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.NavigateBackFromDetail() == true) return;
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        }

        private void PlayitGuidePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _agentService.OnTunnelRunning -= OnTunnelRunning;
        }
    }
}
