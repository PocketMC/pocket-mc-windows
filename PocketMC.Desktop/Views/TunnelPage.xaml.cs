using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    public partial class TunnelPage : Page
    {
        private enum TunnelUiState
        {
            Missing,
            Downloading,
            Ready,
            Starting,
            WaitingForClaim,
            Connected
        }

        private readonly ApplicationState _applicationState;
        private readonly DownloaderService _downloaderService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly ILogger<TunnelPage> _logger;
        private bool _isSubscribed;
        private bool _isDownloading;
        private int _refreshVersion;
        private CancellationTokenSource? _downloadCancellation;
        private TunnelUiState _currentUiState = TunnelUiState.Missing;

        public TunnelPage(
            ApplicationState applicationState,
            DownloaderService downloaderService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            ILogger<TunnelPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _downloaderService = downloaderService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _logger = logger;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            SubscribeToAgent();
            await RefreshStatusAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromAgent();
            CancelDownload();
        }

        private void SubscribeToAgent()
        {
            if (_isSubscribed)
            {
                return;
            }

            _playitAgentService.OnStateChanged += OnPlayitAgentStateChanged;
            _playitAgentService.OnTunnelRunning += OnPlayitTunnelRunning;
            _isSubscribed = true;
        }

        private void UnsubscribeFromAgent()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _playitAgentService.OnStateChanged -= OnPlayitAgentStateChanged;
            _playitAgentService.OnTunnelRunning -= OnPlayitTunnelRunning;
            _isSubscribed = false;
        }

        private void OnPlayitAgentStateChanged(object? sender, PlayitAgentState state)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatusAsync()));
        }

        private void OnPlayitTunnelRunning(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatusAsync()));
        }

        private async Task RefreshStatusAsync()
        {
            int refreshVersion = Interlocked.Increment(ref _refreshVersion);

            if (!_applicationState.IsConfigured)
            {
                SetUiState(TunnelUiState.Missing, "Missing", "PocketMC is not configured with an app root path yet.", Brushes.Orange);
                TxtExecutablePath.Text = "App root not configured";
                ShowNoTunnels("Finish PocketMC setup before managing tunnels.");
                UpdateActionButtons(binaryExists: false);
                return;
            }

            string executablePath = _applicationState.GetPlayitExecutablePath();
            bool binaryExists = File.Exists(executablePath);
            bool partialExists = File.Exists(executablePath + ".partial");

            TxtExecutablePath.Text = executablePath;
            if (!_isDownloading)
            {
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                DownloadProgressBar.IsIndeterminate = false;
            }

            if (_isDownloading)
            {
                SetUiState(TunnelUiState.Downloading, "Downloading", "PocketMC is downloading the Playit.gg agent.", Brushes.DeepSkyBlue);
                ShowNoTunnels("The tunnel list will appear after the agent is downloaded and connected.");
                UpdateActionButtons(binaryExists);
                return;
            }

            if (!binaryExists)
            {
                string detail = partialExists
                    ? "A partial agent download was found. Click Download Agent to resume the transfer."
                    : "playit.exe is missing from the tunnel folder. Download the agent to enable public tunnels.";
                SetUiState(TunnelUiState.Missing, "Missing", detail, Brushes.Orange);
                ShowNoTunnels("Download the Playit agent to begin tunnel setup.");
                UpdateActionButtons(binaryExists: false);
                return;
            }

            switch (_playitAgentService.State)
            {
                case PlayitAgentState.Starting:
                    SetUiState(TunnelUiState.Starting, "Starting", "Launching the Playit agent and waiting for the tunnel service to come online.", Brushes.Gold);
                    ShowNoTunnels("Waiting for the Playit agent to finish starting.");
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.WaitingForClaim:
                    SetUiState(TunnelUiState.WaitingForClaim, "Waiting for Claim", "Approve the Playit claim flow in your browser to finish linking PocketMC, or click Connect to restart the claim flow.", Brushes.Gold);
                    ShowNoTunnels("Complete the claim flow to load your tunnel inventory.");
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.Connected:
                    await RefreshTunnelInventoryAsync(refreshVersion);
                    UpdateActionButtons(binaryExists: true);
                    return;

                case PlayitAgentState.Error:
                case PlayitAgentState.Disconnected:
                case PlayitAgentState.Stopped:
                default:
                    SetUiState(TunnelUiState.Ready, "Ready", "The agent is installed but not connected. Click Connect to retry the tunnel session.", Brushes.Silver);
                    ShowNoTunnels("Connect the Playit agent to load tunnel information.");
                    UpdateActionButtons(binaryExists: true);
                    return;
            }
        }

        private async Task RefreshTunnelInventoryAsync(int refreshVersion)
        {
            try
            {
                TunnelListResult result = await _playitApiClient.GetTunnelsAsync();
                if (refreshVersion != _refreshVersion)
                {
                    return;
                }

                if (result.Success)
                {
                    int tunnelCount = result.Tunnels.Count;
                    string detail = tunnelCount > 0
                        ? $"Connected. {tunnelCount} tunnel{(tunnelCount == 1 ? string.Empty : "s")} currently available."
                        : "Connected. The agent is online, but no tunnels have been created yet.";

                    SetUiState(TunnelUiState.Connected, "Connected", detail, Brushes.LimeGreen);
                    ShowTunnels(
                        result.Tunnels,
                        tunnelCount > 0
                            ? "Tunnel routing is active."
                            : "Create or start a server tunnel to see entries here.");
                    return;
                }

                if (result.RequiresClaim)
                {
                    SetUiState(
                        TunnelUiState.WaitingForClaim,
                        "Waiting for Claim",
                        result.ErrorMessage ?? "PocketMC is waiting for Playit account approval. Click Connect to restart the claim flow if needed.",
                        Brushes.Gold);
                    ShowNoTunnels("Finish the claim flow to load your tunnels.");
                    return;
                }

                if (result.IsTokenInvalid)
                {
                    SetUiState(
                        TunnelUiState.Ready,
                        "Reconnect Required",
                        result.ErrorMessage ?? "The saved Playit credentials were rejected. Click Connect to re-link your agent.",
                        Brushes.Orange);
                    ShowNoTunnels("Tunnel data is unavailable until the agent is linked again.");
                    return;
                }

                SetUiState(TunnelUiState.Connected, "Connected", "The Playit agent is online, but the tunnel API could not be reached right now.", Brushes.LimeGreen);
                ShowNoTunnels(result.ErrorMessage ?? "Tunnel data is temporarily unavailable.");
            }
            catch (Exception ex)
            {
                if (refreshVersion != _refreshVersion)
                {
                    return;
                }

                _logger.LogWarning(ex, "Failed to refresh Playit tunnel inventory.");
                SetUiState(TunnelUiState.Connected, "Connected", "The Playit agent is online, but PocketMC could not refresh the tunnel list.", Brushes.LimeGreen);
                ShowNoTunnels("Retry in a moment or click Refresh to try again.");
            }
        }

        private void SetUiState(TunnelUiState uiState, string status, string detail, Brush foreground)
        {
            _currentUiState = uiState;
            TxtStatusValue.Text = status;
            TxtStatusValue.Foreground = foreground;
            TxtStatusDetail.Text = detail;
        }

        private void ShowNoTunnels(string message)
        {
            TunnelList.ItemsSource = null;
            TunnelList.Visibility = Visibility.Collapsed;
            TxtTunnelListStatus.Text = message;
        }

        private void ShowTunnels(IReadOnlyCollection<TunnelData> tunnels, string message)
        {
            if (tunnels.Count == 0)
            {
                ShowNoTunnels(message);
                return;
            }

            TunnelList.ItemsSource = tunnels;
            TunnelList.Visibility = Visibility.Visible;
            TxtTunnelListStatus.Text = message;
        }

        private void UpdateActionButtons(bool binaryExists)
        {
            bool partialExists = _applicationState.IsConfigured && File.Exists(_applicationState.GetPlayitExecutablePath() + ".partial");

            BtnDownloadAgent.Visibility = binaryExists ? Visibility.Collapsed : Visibility.Visible;
            BtnDownloadAgent.IsEnabled = !_isDownloading;
            BtnDownloadAgent.Content = partialExists ? "Resume Download" : "Download Agent";

            BtnConnect.Content = _currentUiState == TunnelUiState.WaitingForClaim ? "Reconnect" : "Connect";
            BtnConnect.IsEnabled =
                !_isDownloading &&
                binaryExists &&
                _currentUiState is TunnelUiState.Ready or TunnelUiState.WaitingForClaim;

            BtnRefresh.IsEnabled = !_isDownloading;
        }

        private void CancelDownload()
        {
            if (_downloadCancellation == null)
            {
                return;
            }

            if (!_downloadCancellation.IsCancellationRequested)
            {
                _downloadCancellation.Cancel();
            }

            _downloadCancellation.Dispose();
            _downloadCancellation = null;
        }

        private async void BtnDownloadAgent_Click(object sender, RoutedEventArgs e)
        {
            if (!_applicationState.IsConfigured || _isDownloading)
            {
                return;
            }

            CancelDownload();
            _downloadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _downloadCancellation.Token;
            _isDownloading = true;
            DownloadProgressBar.Visibility = Visibility.Visible;
            TxtDownloadProgress.Visibility = Visibility.Visible;
            DownloadProgressBar.IsIndeterminate = true;
            DownloadProgressBar.Value = 0;
            TxtDownloadProgress.Text = "Starting download...";
            SetUiState(TunnelUiState.Downloading, "Downloading", "PocketMC is downloading the Playit.gg agent.", Brushes.DeepSkyBlue);
            UpdateActionButtons(binaryExists: false);

            try
            {
                var progress = new Progress<DownloadProgress>(progressValue =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (progressValue.TotalBytes > 0)
                        {
                            DownloadProgressBar.IsIndeterminate = false;
                            DownloadProgressBar.Value = progressValue.Percentage;
                            TxtDownloadProgress.Text =
                                $"{Math.Round(progressValue.Percentage)}% • {FormatBytes(progressValue.BytesRead)} / {FormatBytes(progressValue.TotalBytes)}";
                        }
                        else
                        {
                            DownloadProgressBar.IsIndeterminate = true;
                            TxtDownloadProgress.Text = $"Downloaded {FormatBytes(progressValue.BytesRead)}...";
                        }
                    });
                });

                await _downloaderService.EnsurePlayitDownloadedAsync(
                    _applicationState.GetRequiredAppRootPath(),
                    progress,
                    cancellationToken);

                TxtDownloadProgress.Text = "Agent downloaded successfully. Click Connect to start Playit.";
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Playit agent download was canceled.");
                TxtDownloadProgress.Text = "Download canceled. You can resume it later.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download the Playit agent.");
                TxtDownloadProgress.Text = $"Download failed: {ex.Message}";
                SetUiState(TunnelUiState.Missing, "Missing", "PocketMC could not finish downloading the Playit agent. Retry when ready.", Brushes.Orange);
            }
            finally
            {
                _isDownloading = false;
                CancelDownload();
                DownloadProgressBar.Visibility = Visibility.Collapsed;

                if (File.Exists(_applicationState.GetPlayitExecutablePath()))
                {
                    TxtDownloadProgress.Visibility = Visibility.Visible;
                }

                await RefreshStatusAsync();
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!_applicationState.IsConfigured || !File.Exists(_applicationState.GetPlayitExecutablePath()) || _isDownloading)
            {
                await RefreshStatusAsync();
                return;
            }

            try
            {
                SetUiState(TunnelUiState.Starting, "Starting", "Launching the Playit agent and waiting for it to connect.", Brushes.Gold);
                ShowNoTunnels("Waiting for the Playit agent to come online.");

                if (_playitAgentService.IsRunning)
                {
                    await _playitAgentService.RestartAsync();
                }
                else
                {
                    _playitAgentService.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Manual Playit connection attempt failed.");
                SetUiState(TunnelUiState.Ready, "Ready", $"PocketMC could not start the agent: {ex.Message}", Brushes.Orange);
            }

            UpdateActionButtons(binaryExists: true);
            await RefreshStatusAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.#} {units[unitIndex]}";
        }
    }
}
