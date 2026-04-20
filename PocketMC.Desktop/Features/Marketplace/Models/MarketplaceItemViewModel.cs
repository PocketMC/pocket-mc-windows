using System;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Marketplace.Models
{
    public enum InstallState
    {
        NotInstalled,
        Installing,
        Installed,
        Failed,
        UpdateAvailable
    }

    public class MarketplaceItemViewModel : ViewModelBase
    {
        private InstallState _state = InstallState.NotInstalled;
        private string? _statusText;
        private bool _isActionEnabled = true;

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string? IconUrl { get; set; }
        public int Downloads { get; set; }
        public string Slug { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Provider { get; set; } = "";

        public InstallState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(StatusBrush));
                    UpdateStatusText();
                }
            }
        }

        public string StatusText
        {
            get => _statusText ?? "";
            set => SetProperty(ref _statusText, value);
        }

        public bool IsActionEnabled
        {
            get => _isActionEnabled;
            set => SetProperty(ref _isActionEnabled, value);
        }

        public string ActionButtonText => State switch
        {
            InstallState.Installing => "Installing...",
            InstallState.Installed => "Reinstall",
            InstallState.UpdateAvailable => "Update",
            InstallState.Failed => "Retry",
            _ => "Install"
        };

        public string StatusBrush => State switch
        {
            InstallState.Installed => "LimeGreen",
            InstallState.UpdateAvailable => "Gold",
            InstallState.Failed => "Red",
            InstallState.Installing => "DeepSkyBlue",
            _ => "Transparent"
        };

        private void UpdateStatusText()
        {
            StatusText = State switch
            {
                InstallState.Installed => "Installed",
                InstallState.UpdateAvailable => "Update Available",
                InstallState.Failed => "Install Failed",
                _ => ""
            };
        }
    }
}
