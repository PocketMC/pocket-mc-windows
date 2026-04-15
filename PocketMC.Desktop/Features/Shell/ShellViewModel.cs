using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Features.Shell
{
    public class ShellViewModel : ViewModelBase
    {
        private string _updateStatusMessage = "Checking for updates...";
        private bool _isUpdateDownloading;
        private double _updateDownloadPercent;
        private string? _updateErrorMessage;
        public string UpdateStatusMessage
        {
            get => _updateStatusMessage;
            private set => SetProperty(ref _updateStatusMessage, value);
        }

        private bool _isUpdateBannerVisible;
        public bool IsUpdateBannerVisible
        {
            get => _isUpdateBannerVisible;
            set => SetProperty(ref _isUpdateBannerVisible, value);
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
            IsUpdateBannerVisible ? Visibility.Visible : Visibility.Collapsed;

        private readonly IShellUIStateService _uiStateService;
        private readonly UpdateService _updateService;
        private readonly IApplicationLifecycleService _applicationLifecycle;
        private readonly ResourceMonitorService _resourceMonitorService;
        private readonly ILogger<ShellViewModel> _logger;

        private bool _isNavigationLocked;
        private bool _isPaneVisible = true;
        private bool _isPaneToggleVisible = true;

        public ICommand RestartAndApplyUpdateCommand { get; }
        public ICommand DismissUpdateBannerCommand { get; }

        public ShellViewModel(
            IShellUIStateService uiStateService,
            UpdateService updateService,
            IApplicationLifecycleService applicationLifecycle,
            ResourceMonitorService resourceMonitorService,
            ILogger<ShellViewModel> logger)
        {
            _uiStateService = uiStateService;
            _updateService = updateService;
            _applicationLifecycle = applicationLifecycle;
            _resourceMonitorService = resourceMonitorService;
            _logger = logger;

            RestartAndApplyUpdateCommand = new RelayCommand(async _ => await ExecuteRestartAndApplyUpdateAsync());
            DismissUpdateBannerCommand = new RelayCommand(_ => IsUpdateBannerVisible = false);
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

            _resourceMonitorService.MetricsUpdated += OnMetricsUpdated;
            _updateService.OnStatusChanged += OnUpdateStatusChanged;
            InitializeUpdateCheck();
        }

        private void OnMetricsUpdated(object? sender, EventArgs e)
        {
            var summary = _resourceMonitorService.CurrentSummary;
            if (Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                Application.Current.Dispatcher.BeginInvoke(() => {
                    _uiStateService.GlobalHealthStatusText = summary.DisplayText;
                    _uiStateService.GlobalHealthStatusBrush = summary.IsHighUsage
                        ? System.Windows.Media.Brushes.Red
                        : System.Windows.Media.Brushes.White;
                });
                return;
            }

            _uiStateService.GlobalHealthStatusText = summary.DisplayText;
            _uiStateService.GlobalHealthStatusBrush = summary.IsHighUsage
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.White;
        }

        public async Task CheckForUpdatesAsync()
        {
            await _updateService.CheckAndDownloadAsync();
        }

        public void InitializeUpdateCheck()
        {
            Task.Run(() => _updateService.CheckAndDownloadAsync());
        }

        private async Task ExecuteRestartAndApplyUpdateAsync()
        {
            if (!_updateService.HasPendingUpdate) return;

            UpdateStatusMessage = "Shutting down active servers...";
            IsUpdateDownloading = true; // Use this as a general "busy" state for the banner
            
            await _applicationLifecycle.GracefulShutdownAsync();

            UpdateStatusMessage = "Ready to update...";
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
                    IsUpdateBannerVisible = true;
                    IsUpdateDownloading = false;
                    UpdateStatusMessage = "Checking for updates...";
                    UpdateErrorMessage = null;
                    break;

                case UpdateStage.Downloading:
                    IsUpdateBannerVisible = true;
                    IsUpdateDownloading = true;
                    UpdateStatusMessage = $"Downloading update {status.NewVersion}...";
                    UpdateDownloadPercent = status.DownloadPercent;
                    UpdateErrorMessage = null;
                    break;

                case UpdateStage.ReadyToRestart:
                    IsUpdateBannerVisible = true;
                    IsUpdateDownloading = false;
                    UpdateStatusMessage = $"PocketMC {status.NewVersion} is ready to install";
                    UpdateDownloadPercent = 100;
                    UpdateErrorMessage = null;
                    OnPropertyChanged(nameof(IsUpdateReadyToApply));
                    _logger.LogInformation("Update {Version} is ready to apply.", status.NewVersion);
                    break;

                case UpdateStage.UpToDate:
                case UpdateStage.Idle:
                    if (status.Stage == UpdateStage.Idle && _updateService.HasPendingUpdate)
                    {
                        // Keep banner if we already have an update ready
                        break;
                    }
                    IsUpdateBannerVisible = false;
                    IsUpdateDownloading = false;
                    UpdateStatusMessage = string.Empty;
                    UpdateErrorMessage = null;
                    break;

                case UpdateStage.Error:
                    IsUpdateDownloading = false;
                    UpdateErrorMessage = status.ErrorMessage;
                    UpdateStatusMessage = $"Update error: {status.ErrorMessage}";
                    _logger.LogWarning("Update pipeline error: {Error}", status.ErrorMessage);
                    // Keep banner visible to show error? or hide it?
                    // Let's keep it for a few seconds then hide or let user dismiss.
                    break;
            }
            
            OnPropertyChanged(nameof(UpdateBannerVisibility));
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
