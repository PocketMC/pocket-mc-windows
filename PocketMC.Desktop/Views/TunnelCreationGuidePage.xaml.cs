using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    public partial class TunnelCreationGuidePage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly TunnelService _tunnelService;
        private readonly int _serverPort;
        private CancellationTokenSource? _pollingCts;
        private int _closeRequested;

        public event Action<string>? OnTunnelResolved;
        public string? ResolvedAddress { get; private set; }

        public TunnelCreationGuidePage(
            IAppNavigationService navigationService,
            TunnelService tunnelService,
            int serverPort)
        {
            InitializeComponent();
            _navigationService = navigationService;
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
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = $"✓ Tunnel found: {address}";
                    });

                    await Task.Delay(600, token);
                    await Dispatcher.InvokeAsync(RequestClose);
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = "Timed out waiting for tunnel.";
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = $"Error: {ex.Message}";
                });
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            RequestClose();
        }

        private void TunnelCreationGuidePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private void RequestClose()
        {
            if (Interlocked.Exchange(ref _closeRequested, 1) != 0)
            {
                return;
            }

            _pollingCts?.Cancel();

            if (_navigationService.NavigateBack())
            {
                return;
            }

            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.NavigateBackFromDetail() == true)
            {
                return;
            }

            if (NavigationService?.CanGoBack == true)
            {
                NavigationService.GoBack();
            }
        }
    }
}
