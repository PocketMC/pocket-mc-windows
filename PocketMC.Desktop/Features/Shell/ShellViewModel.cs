using System.Windows;
using System.Windows.Media;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Shell
{
    public class ShellViewModel : ViewModelBase
    {
        private readonly IShellUIStateService _uiStateService;
        private bool _isNavigationLocked;
        private bool _isPaneVisible = true;
        private bool _isPaneToggleVisible = true;

        public ShellViewModel(IShellUIStateService uiStateService)
        {
            _uiStateService = uiStateService;
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
