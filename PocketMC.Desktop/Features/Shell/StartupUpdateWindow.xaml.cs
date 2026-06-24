using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Shell
{
    public partial class StartupUpdateWindow : Window, INotifyPropertyChanged
    {
        private readonly UpdateService _updateService;

        private string _statusMessage = "Checking for updates...";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        private string _currentVersion = "Unknown";
        public string CurrentVersion
        {
            get => _currentVersion;
            set { _currentVersion = value; OnPropertyChanged(nameof(CurrentVersion)); }
        }

        private string _latestVersion = "Unknown";
        public string LatestVersion
        {
            get => _latestVersion;
            set { _latestVersion = value; OnPropertyChanged(nameof(LatestVersion)); }
        }

        private double _downloadPercent;
        public double DownloadPercent
        {
            get => _downloadPercent;
            set { _downloadPercent = value; OnPropertyChanged(nameof(DownloadPercent)); }
        }

        private string _downloadSizeText = "";
        public string DownloadSizeText
        {
            get => _downloadSizeText;
            set { _downloadSizeText = value; OnPropertyChanged(nameof(DownloadSizeText)); }
        }

        private bool _isChecking = true;
        public bool IsChecking
        {
            get => _isChecking;
            set { _isChecking = value; OnPropertyChanged(nameof(IsChecking)); }
        }

        private bool _isDownloading = false;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(nameof(IsDownloading)); }
        }

        private bool _isUpdateInfoVisible = false;
        public bool IsUpdateInfoVisible
        {
            get => _isUpdateInfoVisible;
            set { _isUpdateInfoVisible = value; OnPropertyChanged(nameof(IsUpdateInfoVisible)); }
        }

        private bool _hasError = false;
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(nameof(HasError)); }
        }

        public bool ShouldContinueToApp { get; private set; } = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public StartupUpdateWindow(UpdateService updateService)
        {
            InitializeComponent();
            DataContext = this;

            _updateService = updateService;
            _updateService.OnStatusChanged += OnUpdateStatusChanged;

            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version != null)
            {
                CurrentVersion = $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public async void StartUpdateCheck()
        {
            await _updateService.CheckAndDownloadAsync();
        }

        private void OnUpdateStatusChanged(UpdateStatus status)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                Application.Current.Dispatcher.BeginInvoke(() => OnUpdateStatusChanged(status));
                return;
            }

            switch (status.Stage)
            {
                case UpdateStage.Checking:
                    StatusMessage = "Checking for updates...";
                    IsChecking = true;
                    IsDownloading = false;
                    IsUpdateInfoVisible = false;
                    HasError = false;
                    break;

                case UpdateStage.Downloading:
                    if (!IsDownloading)
                    {
                        StatusMessage = "Downloading update...";
                    }
                    else
                    {
                        StatusMessage = $"Downloading update... {status.DownloadPercent:F1}%";
                    }

                    if (status.NewVersion != null) LatestVersion = status.NewVersion;

                    if (status.DownloadSize.HasValue)
                    {
                        double mb = status.DownloadSize.Value / 1024.0 / 1024.0;
                        DownloadSizeText = $"Download Size: {mb:F1} MB";
                    }

                    DownloadPercent = status.DownloadPercent;
                    IsChecking = false;
                    IsDownloading = true;
                    IsUpdateInfoVisible = true;
                    HasError = false;
                    break;

                case UpdateStage.ReadyToRestart:
                    StatusMessage = "Installing update...";
                    DownloadPercent = 100;
                    IsChecking = false;
                    IsDownloading = true;
                    IsUpdateInfoVisible = true;
                    HasError = false;
                    ShouldContinueToApp = false;
                    // AutomaticallyInstallUpdates is handled by the caller or by UpdateService itself.
                    // But wait, UpdateService automatically calls ApplyUpdateAndRestart if settings true!
                    // We just need to close the window or let the app restart.
                    // If it restarts, the process will be killed. We can just wait.
                    break;

                case UpdateStage.UpToDate:
                    ShouldContinueToApp = true;
                    Close();
                    break;

                case UpdateStage.Idle:
                    if (!ShouldContinueToApp) break; // It was ready to restart
                    ShouldContinueToApp = true;
                    Close();
                    break;

                case UpdateStage.Error:
                    StatusMessage = $"Update Error: {status.ErrorMessage}";
                    IsChecking = false;
                    IsDownloading = false;
                    HasError = true;
                    ShouldContinueToApp = true;
                    break;
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldContinueToApp = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateService.OnStatusChanged -= OnUpdateStatusChanged;
            base.OnClosed(e);
        }
    }
}
