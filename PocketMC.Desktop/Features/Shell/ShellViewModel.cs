using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using Velopack;
using Velopack.Exceptions;

namespace PocketMC.Desktop.Features.Shell
{
    public class ShellViewModel : ViewModelBase
    {
        private const string VelopackUpdateSource = "https://github.com/PocketMC/pocket-mc-windows";
        private readonly IShellUIStateService _uiStateService;
        private readonly ILogger<ShellViewModel> _logger;
        private bool _isNavigationLocked;
        private bool _isPaneVisible = true;
        private bool _isPaneToggleVisible = true;

        public ShellViewModel(IShellUIStateService uiStateService, ILogger<ShellViewModel> logger)
        {
            _uiStateService = uiStateService;
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
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var mgr = new UpdateManager(VelopackUpdateSource);
                var updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
                if (updateInfo is null)
                {
                    _logger.LogDebug("Velopack update check completed with no updates available.");
                    return;
                }

                _logger.LogInformation("Velopack update found: {Version}. Downloading and applying.", updateInfo.TargetFullRelease.Version);
                await mgr.DownloadUpdatesAsync(updateInfo).ConfigureAwait(false);
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (NotInstalledException)
            {
                _logger.LogDebug("Velopack update check skipped because the app is not installed yet.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Velopack update check failed.");
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
