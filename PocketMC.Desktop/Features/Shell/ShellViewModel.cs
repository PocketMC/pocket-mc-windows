using System.Windows;
using System.Windows.Media;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Features.Shell
{
    public class ShellViewModel : ViewModelBase
    {
        private readonly IShellUIStateService _uiStateService;
        private readonly UpdateService _updateService;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ILogger<ShellViewModel> _logger;

        private bool _isNavigationLocked;
        private bool _isPaneVisible = true;
        private bool _isPaneToggleVisible = true;

        private bool _isUpdateAvailable;
        private string? _updateVersion;
        private bool _isUpdateDownloading;
        private double _updateDownloadPercent;
        private string? _updateErrorMessage;

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            internal set { if (SetProperty(ref _isUpdateAvailable, value)) OnPropertyChanged(nameof(UpdateBannerVisibility)); }
        }

        public string? UpdateVersion
        {
            get => _updateVersion;
            private set => SetProperty(ref _updateVersion, value);
        }

        public bool IsUpdateDownloading
        {
            get => _isUpdateDownloading;
            private set => SetProperty(ref _isUpdateDownloading, value);
        }

        public double UpdateDownloadPercent
        {
            get => _updateDownloadPercent;
            private set => SetProperty(ref _updateDownloadPercent, value);
        }

        public string? UpdateErrorMessage
        {
            get => _updateErrorMessage;
            private set => SetProperty(ref _updateErrorMessage, value);
        }

        public bool IsUpdateReadyToApply => _updateService.HasPendingUpdate && !IsUpdateDownloading;

        public Visibility UpdateBannerVisibility =>
            IsUpdateAvailable || IsUpdateDownloading ? Visibility.Visible : Visibility.Collapsed;

        public bool CanApplyUpdate =>
            _updateService.HasPendingUpdate &&
            _serverProcessManager.ActiveProcesses.IsEmpty;

        public ShellViewModel(
            IShellUIStateService uiStateService,
            UpdateService updateService,
            ServerProcessManager serverProcessManager,
            ILogger<ShellViewModel> logger)
        {
            _uiStateService = uiStateService;
            _updateService = updateService;
            _serverProcessManager = serverProcessManager;
            _logger = logger;

            _uiStateService.OnStateChanged += () =>
            {
                OnPropertyChanged(nameof(BreadcrumbCurrentText));
                OnPropertyChanged(nameof(IsBreadcrumbVisible));
                OnPropertyChanged(nameof(BreadcrumbVisibility));
                OnPropertyChanged(nameof(TitleBarTitle));
                OnPropertyChanged(nameof(TitleBarStatusText));
                OnPropertyChanged(nameof(TitleBarStatusBrush));
                OnPropertyChanged(nameof(IsTitleBarContextVisible));
                OnPropertyChanged(nameof(TitleBarContextVisibility));
                OnPropertyChanged(nameof(GlobalHealthStatusText));
                OnPropertyChanged(nameof(GlobalHealthStatusBrush));
                OnPropertyChanged(nameof(GlobalHealthVisibility));
            };

            _updateService.OnStatusChanged += OnUpdateStatusChanged;
        }

        public async Task CheckForUpdatesAsync()
        {
            await _updateService.CheckAndDownloadAsync();
        }

        public void RequestApplyUpdate()
        {
            if (!_updateService.HasPendingUpdate) return;

            if (!_serverProcessManager.ActiveProcesses.IsEmpty)
            {
                _logger.LogInformation(
                    "Update restart deferred — {Count} server(s) still running.",
                    _serverProcessManager.ActiveProcesses.Count);
                return;
            }

            _updateService.ApplyUpdateAndRestart();
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
                    IsUpdateDownloading = false;
                    UpdateErrorMessage = null;
                    break;

                case UpdateStage.Downloading:
                    IsUpdateAvailable = true;
                    IsUpdateDownloading = true;
                    UpdateVersion = status.NewVersion;
                    UpdateDownloadPercent = status.DownloadPercent;
                    UpdateErrorMessage = null;
                    break;

                case UpdateStage.ReadyToRestart:
                    IsUpdateAvailable = true;
                    IsUpdateDownloading = false;
                    UpdateVersion = status.NewVersion;
                    UpdateDownloadPercent = 100;
                    UpdateErrorMessage = null;
                    OnPropertyChanged(nameof(IsUpdateReadyToApply));
                    OnPropertyChanged(nameof(CanApplyUpdate));
                    _logger.LogInformation("Update {Version} is ready to apply.", status.NewVersion);
                    break;

                case UpdateStage.UpToDate:
                case UpdateStage.Idle:
                    IsUpdateAvailable = false;
                    IsUpdateDownloading = false;
                    UpdateErrorMessage = null;
                    break;

                case UpdateStage.Error:
                    IsUpdateDownloading = false;
                    UpdateErrorMessage = status.ErrorMessage;
                    _logger.LogWarning("Update pipeline error: {Error}", status.ErrorMessage);
                    IsUpdateAvailable = false;
                    break;
            }
        }

        public string? BreadcrumbCurrentText => _uiStateService.BreadcrumbCurrentText;
        public bool IsBreadcrumbVisible => _uiStateService.IsBreadcrumbVisible;

        public string? TitleBarTitle => _uiStateService.TitleBarTitle;
        public string? TitleBarStatusText => _uiStateService.TitleBarStatusText;
        public Brush? TitleBarStatusBrush => _uiStateService.TitleBarStatusBrush;
        public bool IsTitleBarContextVisible => _uiStateService.IsTitleBarContextVisible;

        public string? GlobalHealthStatusText => _uiStateService.GlobalHealthStatusText;
        public Brush? GlobalHealthStatusBrush => _uiStateService.GlobalHealthStatusBrush;

        public bool IsNavigationLocked
        {
            get => _isNavigationLocked;
            set { if (SetProperty(ref _isNavigationLocked, value)) OnPropertyChanged(nameof(GlobalHealthVisibility)); }
        }

        public bool IsPaneVisible
        {
            get => _isPaneVisible;
            set { if (SetProperty(ref _isPaneVisible, value)) OnPropertyChanged(nameof(NavigationVisibility)); }
        }

        public bool IsPaneToggleVisible
        {
            get => _isPaneToggleVisible;
            set => SetProperty(ref _isPaneToggleVisible, value);
        }

        public Visibility BreadcrumbVisibility => IsBreadcrumbVisible ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TitleBarContextVisibility => IsTitleBarContextVisible ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlobalHealthVisibility => !IsNavigationLocked ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavigationVisibility => IsPaneVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
