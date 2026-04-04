using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    /// <summary>
    /// Code-behind for TunnelCreationGuideWindow.
    /// Polls the Playit API every 5s for a new tunnel matching the server port.
    /// Auto-closes when the tunnel is detected (NET-08).
    /// </summary>
    public partial class TunnelCreationGuideWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly TunnelService _tunnelService;
        private readonly int _serverPort;
        private CancellationTokenSource? _pollingCts;

        /// <summary>
        /// Fires when the tunnel starts successfully and its address is resolved locally.
        /// </summary>
        public event Action<string>? OnTunnelResolved;

        /// <summary>
        /// The public address of the newly created tunnel, populated when found.
        /// </summary>
        public string? ResolvedAddress { get; private set; }

        public TunnelCreationGuideWindow(TunnelService tunnelService, int serverPort)
        {
            InitializeComponent();
            _tunnelService = tunnelService;
            _serverPort = serverPort;

            // Display the port in the instructions
            PortValueRun.Text = serverPort.ToString();

            // Start polling in the background
            _pollingCts = new CancellationTokenSource();
            _ = PollForTunnelAsync(_pollingCts.Token);
        }

        private async Task PollForTunnelAsync(CancellationToken token)
        {
            try
            {
                string? address = await _tunnelService.PollForNewTunnelAsync(_serverPort, token);

                if (address != null)
                {
                    ResolvedAddress = address;
                    OnTunnelResolved?.Invoke(address);
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"✓ Tunnel found: {address}";
                        Close();
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Timed out waiting for tunnel.";
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Error: {ex.Message}";
                });
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _pollingCts?.Cancel();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            base.OnClosed(e);
        }
    }
}
