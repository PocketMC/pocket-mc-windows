using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel wrapper for InstanceMetadata that adds live state tracking.
    /// </summary>
    public class InstanceCardViewModel : INotifyPropertyChanged
    {
        private readonly InstanceMetadata _metadata;
        private readonly ServerProcessManager _serverProcessManager;
        private ServerState _state = ServerState.Stopped;
        private string? _countdownText;
        private string _playerStatus = "0 Players Online";
        private string _cpuText = "CPU 0%";
        private string _ramText = "RAM 0 MB";
        private string? _tunnelAddress;

        public InstanceCardViewModel(InstanceMetadata metadata, ServerProcessManager serverProcessManager)
        {
            _metadata = metadata;
            _serverProcessManager = serverProcessManager;

            if (_serverProcessManager.IsRunning(metadata.Id))
            {
                var proc = _serverProcessManager.GetProcess(metadata.Id);
                _state = proc?.State ?? ServerState.Stopped;
            }
        }

        public InstanceMetadata Metadata => _metadata;
        public Guid Id => _metadata.Id;
        public string Name => _metadata.Name;
        public string Description => _metadata.Description;
        public bool IsRunning => _state == ServerState.Starting || _state == ServerState.Online || _state == ServerState.Stopping;
        public bool IsWaitingToRestart => _serverProcessManager.IsWaitingToRestart(Id);
        public bool ShowRunningControls => IsRunning || IsWaitingToRestart;
        public string StopButtonText => IsWaitingToRestart ? "Abort" : "Stop";

        public string StatusText => _countdownText ?? _state switch
        {
            ServerState.Stopped => "● Stopped",
            ServerState.Starting => "◉ Starting...",
            ServerState.Online => "● Online",
            ServerState.Stopping => "◉ Stopping...",
            ServerState.Crashed => "✖ Crashed",
            _ => "Unknown"
        };

        public Brush StatusColor => _state switch
        {
            ServerState.Online => Brushes.LimeGreen,
            ServerState.Starting or ServerState.Stopping => Brushes.Orange,
            ServerState.Crashed => Brushes.Red,
            _ => Brushes.Gray
        };

        public ObservableCollection<double> CpuHistory { get; } = new();
        public ObservableCollection<double> RamHistory { get; } = new();
        public LiveChartsCore.ISeries[]? CpuSeries { get; set; }
        public LiveChartsCore.ISeries[]? RamSeries { get; set; }
        public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint InvisiblePaint { get; set; } = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Transparent);
        public LiveChartsCore.SkiaSharpView.Axis[] InvisibleXAxes { get; set; } =
            new[]
            {
                new LiveChartsCore.SkiaSharpView.Axis { IsVisible = false, ShowSeparatorLines = false }
            };
        public LiveChartsCore.SkiaSharpView.Axis[] InvisibleYAxes { get; set; } =
            new[]
            {
                new LiveChartsCore.SkiaSharpView.Axis { IsVisible = false, MinLimit = 0, ShowSeparatorLines = false }
            };

        public string PlayerStatus
        {
            get => _playerStatus;
            set
            {
                _playerStatus = value;
                OnPropertyChanged(nameof(PlayerStatus));
            }
        }

        public string CpuText
        {
            get => _cpuText;
            set
            {
                _cpuText = value;
                OnPropertyChanged(nameof(CpuText));
            }
        }

        public string RamText
        {
            get => _ramText;
            set
            {
                _ramText = value;
                OnPropertyChanged(nameof(RamText));
            }
        }

        public string? TunnelAddress
        {
            get => _tunnelAddress;
            set
            {
                _tunnelAddress = value;
                OnPropertyChanged(nameof(TunnelAddress));
                OnPropertyChanged(nameof(HasTunnelAddress));
            }
        }

        public bool HasTunnelAddress => !string.IsNullOrEmpty(_tunnelAddress);

        public void EnsureChartsReady()
        {
            if (CpuSeries != null)
            {
                return;
            }

            CpuSeries =
            new LiveChartsCore.ISeries[]
            {
                new LiveChartsCore.SkiaSharpView.LineSeries<double>
                {
                    Values = CpuHistory,
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#204CAF50")),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#4CAF50")) { StrokeThickness = 2 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0.5
                }
            };

            RamSeries =
            new LiveChartsCore.ISeries[]
            {
                new LiveChartsCore.SkiaSharpView.LineSeries<double>
                {
                    Values = RamHistory,
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#202196F3")),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#2196F3")) { StrokeThickness = 2 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0.5
                }
            };

            OnPropertyChanged(nameof(CpuSeries));
            OnPropertyChanged(nameof(RamSeries));
        }

        public void UpdateState(ServerState newState)
        {
            _countdownText = null;
            _state = newState;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(IsWaitingToRestart));
            OnPropertyChanged(nameof(StopButtonText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        public void UpdateCountdown(int seconds)
        {
            _countdownText = $"Restarting in {seconds}s...";
            _state = ServerState.Crashed;
            OnPropertyChanged(nameof(IsWaitingToRestart));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(StopButtonText));
            OnPropertyChanged(nameof(StatusText));
        }

        public void RefreshNameDescription()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
