using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    public partial class TunnelCreationGuidePage : Page
    {
        private readonly TunnelService _tunnelService;
        private readonly int _serverPort;
        private CancellationTokenSource? _pollingCts;

        public event Action<string>? OnTunnelResolved;
        public string? ResolvedAddress { get; private set; }

        public TunnelCreationGuidePage(TunnelService tunnelService, int serverPort)
        {
            InitializeComponent();
            _tunnelService = tunnelService;
            _serverPort = serverPort;

            PortValueRun.Text = serverPort.ToString();

            _pollingCts = new CancellationTokenSource();
            _ = PollForTunnelAsync(_pollingCts.Token);

            Unloaded += TunnelCreationGuidePage_Unloaded;
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
                        if (NavigationService.CanGoBack)
                        {
                            NavigationService.GoBack();
                        }
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

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            _pollingCts?.Cancel();
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.NavigateBackFromDetail() == true) return;
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        }

        private void TunnelCreationGuidePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }
    }
}
