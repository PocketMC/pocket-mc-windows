using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PocketMC.Application.Interfaces.Tunnels;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models.Tunnel;

namespace PocketMC.Desktop.Features.Tunnel
{
    public partial class PlayitStatusViewModel : ViewModelBase
    {
        private readonly IPlayitStatusService _playitStatusService;
        private readonly IAppNavigationService _navigationService;

        public ObservableCollection<PlayitStatusMonitorViewModel> Monitors { get; } = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string? _lastUpdated;
        public string? LastUpdated
        {
            get => _lastUpdated;
            set => SetProperty(ref _lastUpdated, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand GoBackCommand { get; }

        public PlayitStatusViewModel(IPlayitStatusService playitStatusService, IAppNavigationService navigationService)
        {
            _playitStatusService = playitStatusService;
            _navigationService = navigationService;
            RefreshCommand = new AsyncRelayCommand(async (o) => await LoadStatusAsync());
            GoBackCommand = new RelayCommand((o) => GoBack());
        }

        public async Task LoadStatusAsync()
        {
            if (IsLoading) return;
            IsLoading = true;

            try
            {
                var statusData = await _playitStatusService.GetNetworkStatusAsync(CancellationToken.None);
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Monitors.Clear();
                    foreach (var monitor in statusData)
                    {
                        Monitors.Add(new PlayitStatusMonitorViewModel(monitor));
                    }
                    LastUpdated = $"Last updated: {DateTime.Now:t}";
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void GoBack()
        {
            _navigationService.NavigateBack();
        }
    }

    public class PlayitStatusMonitorViewModel
    {
        public string Name { get; }
        public string StatusClass { get; }
        public string StatusText { get; }
        public Brush? IconColor { get; }

        public PlayitStatusMonitorViewModel(PlayitStatusMonitor monitor)
        {
            Name = monitor.Name ?? "Unknown Node";
            StatusClass = monitor.StatusClass ?? "unknown";

            string brushKey;
            switch (StatusClass.ToLowerInvariant())
            {
                case "success":
                    StatusText = "Operational";
                    brushKey = "SystemFillColorSuccessBrush";
                    break;
                case "warning":
                    StatusText = "Degraded";
                    brushKey = "SystemFillColorCautionBrush";
                    break;
                case "danger":
                    StatusText = "Outage";
                    brushKey = "SystemFillColorCriticalBrush";
                    break;
                default:
                    StatusText = "Unknown";
                    brushKey = "TextFillColorSecondaryBrush";
                    break;
            }

            if (System.Windows.Application.Current != null && System.Windows.Application.Current.Resources.Contains(brushKey))
            {
                IconColor = System.Windows.Application.Current.Resources[brushKey] as Brush;
            }
        }
    }
}
