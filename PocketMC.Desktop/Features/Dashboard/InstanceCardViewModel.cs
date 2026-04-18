using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Dashboard;

public class InstanceCardViewModel : INotifyPropertyChanged
{
    private InstanceMetadata _metadata;
    private readonly ServerProcessManager _serverProcessManager;
    private readonly IServerLifecycleService _lifecycleService;
    private ServerState _state = ServerState.Stopped;
    private string? _countdownText;
    private string _cpuText = "· · ·";
    private string _ramText = "· · ·";
    private string _playerStatus = "· · ·";
    private string? _tunnelAddress;
    private string? _bedrockTunnelAddress;
    private string? _bedrockIpDisplayTextOverride;
    private int _bedrockLocalPort;
    private string _ipDisplayText = "Will Appear Here!";
    private string? _portIssueText;
    private string? _portIssueTooltip;

    public InstanceCardViewModel(InstanceMetadata metadata, ServerProcessManager serverProcessManager, IServerLifecycleService lifecycleService)
    {
        _metadata = metadata;
        _serverProcessManager = serverProcessManager;
        _lifecycleService = lifecycleService;
        _bedrockLocalPort = metadata.GeyserBedrockPort ?? 19132;

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
    public bool IsWaitingToRestart => _lifecycleService.IsWaitingToRestart(Id);
    public bool ShowRunningControls => IsRunning || IsWaitingToRestart;
    public Visibility RunningControlsVisibility => ShowRunningControls ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StoppedControlsVisibility => ShowRunningControls ? Visibility.Collapsed : Visibility.Visible;
    public string StopButtonText => IsWaitingToRestart ? "Abort" : "Stop";
    public string MinecraftVersion => _metadata.MinecraftVersion;
    public string ServerType => _metadata.ServerType;
    public int MaxPlayers => _metadata.MaxPlayers;
    public bool HasTunnelAddress => !string.IsNullOrEmpty(_tunnelAddress);
    public bool HasPortIssue => !string.IsNullOrWhiteSpace(_portIssueText);
    public Visibility PortIssueVisibility => HasPortIssue ? Visibility.Visible : Visibility.Collapsed;
    public string? PortIssueText => _portIssueText;
    public string? PortIssueTooltip => _portIssueTooltip;

    /// <summary>True for native Bedrock servers (BDS, Pocketmine).</summary>
    public bool IsBedrockServer =>
        _metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true ||
        _metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when a Java server has Geyser cross-play enabled.</summary>
    public bool HasGeyser => _metadata.HasGeyser;

    /// <summary>Whether to show a separate Bedrock IP row on the card.
    /// Only meaningful for Java servers with Geyser cross-play enabled, where
    /// Java players and Bedrock players reach the server on different addresses.
    /// Native BDS / PocketMine servers have a single address — no second row needed.</summary>
    public bool ShowBedrockIp => !IsBedrockServer && HasGeyser;

    /// <summary>Label prefix for the secondary IP row.</summary>
    public string BedrockIpLabel => "Bedrock (Geyser):";

    public int BedrockLocalPort => _bedrockLocalPort;

    /// <summary>Text for the Bedrock IP row (tunnel address, or local Geyser port note).</summary>
    public string BedrockIpDisplayText
    {
        get
        {
            if (_bedrockIpDisplayTextOverride != null) return _bedrockIpDisplayTextOverride;

            if (IsBedrockServer && !string.IsNullOrEmpty(_tunnelAddress))
            {
                return _tunnelAddress;
            }
            if (HasGeyser && !string.IsNullOrEmpty(_bedrockTunnelAddress))
            {
                return _bedrockTunnelAddress;
            }
            return $"127.0.0.1:{_bedrockLocalPort} (local)";
        }
        set
        {
            _bedrockIpDisplayTextOverride = value;
            OnPropertyChanged(nameof(BedrockIpDisplayText));
        }
    }

    public string StatusText => _countdownText ?? _state switch
    {
        ServerState.Stopped => "● Stopped",
        ServerState.Starting => "● Starting",
        ServerState.Online => "● Online",
        ServerState.Stopping => "● Stopping",
        ServerState.Crashed => "⚠️ Crashed",
        _ => "● Unknown"
    };

    public Brush StatusBrush => _state switch
    {
        ServerState.Online => Brushes.LimeGreen,
        ServerState.Starting => Brushes.SkyBlue,
        ServerState.Stopping => Brushes.Orange,
        ServerState.Crashed => Brushes.Red,
        _ => Brushes.DarkGray
    };

    public string CpuText { get => _cpuText; set { if (_cpuText != value) { _cpuText = value; OnPropertyChanged(nameof(CpuText)); } } }
    public string RamText { get => _ramText; set { if (_ramText != value) { _ramText = value; OnPropertyChanged(nameof(RamText)); } } }
    public string PlayerStatus { get => _playerStatus; set { if (_playerStatus != value) { _playerStatus = value; OnPropertyChanged(nameof(PlayerStatus)); } } }
    public string IpDisplayText { get => _ipDisplayText; set { if (_ipDisplayText != value) { _ipDisplayText = value; OnPropertyChanged(nameof(IpDisplayText)); } } }

    public string? TunnelAddress
    {
        get => _tunnelAddress;
        set
        {
            if (SetProperty(ref _tunnelAddress, value))
            {
                OnPropertyChanged(nameof(DisplayAddress));
                OnPropertyChanged(nameof(HasTunnelAddress));
                OnPropertyChanged(nameof(BedrockIpDisplayText));
                UpdateIpDisplay();
            }
        }
    }

    public string? BedrockTunnelAddress
    {
        get => _bedrockTunnelAddress;
        set
        {
            if (SetProperty(ref _bedrockTunnelAddress, value))
            {
                OnPropertyChanged(nameof(BedrockIpDisplayText));
            }
        }
    }

    public string DisplayAddress => _tunnelAddress ?? "127.0.0.1";

    public void UpdateState(ServerState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(RunningControlsVisibility));
            OnPropertyChanged(nameof(StoppedControlsVisibility));
            OnPropertyChanged(nameof(StopButtonText));
        }
    }

    public void SetPortIssue(string badgeText, string tooltip)
    {
        _portIssueText = badgeText;
        _portIssueTooltip = tooltip;
        OnPropertyChanged(nameof(HasPortIssue));
        OnPropertyChanged(nameof(PortIssueVisibility));
        OnPropertyChanged(nameof(PortIssueText));
        OnPropertyChanged(nameof(PortIssueTooltip));
    }

    public void ClearPortIssue()
    {
        if (_portIssueText == null && _portIssueTooltip == null)
        {
            return;
        }

        _portIssueText = null;
        _portIssueTooltip = null;
        OnPropertyChanged(nameof(HasPortIssue));
        OnPropertyChanged(nameof(PortIssueVisibility));
        OnPropertyChanged(nameof(PortIssueText));
        OnPropertyChanged(nameof(PortIssueTooltip));
    }

    public void SetBedrockLocalPort(int port)
    {
        if (port <= 0 || port > 65535 || _bedrockLocalPort == port)
        {
            return;
        }

        _bedrockLocalPort = port;
        OnPropertyChanged(nameof(BedrockLocalPort));
        OnPropertyChanged(nameof(BedrockIpDisplayText));
    }

    public void UpdateCountdown(int secondsLeft)
    {
        _countdownText = $"🔄 Restarting in {secondsLeft}s...";
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowRunningControls));
        OnPropertyChanged(nameof(RunningControlsVisibility));
        OnPropertyChanged(nameof(StoppedControlsVisibility));
        OnPropertyChanged(nameof(StopButtonText));
    }

    public void ClearCountdown()
    {
        _countdownText = null;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowRunningControls));
        OnPropertyChanged(nameof(RunningControlsVisibility));
        OnPropertyChanged(nameof(StoppedControlsVisibility));
        OnPropertyChanged(nameof(StopButtonText));
    }

    private void UpdateIpDisplay()
    {
        if (!string.IsNullOrEmpty(_tunnelAddress))
        {
            IpDisplayText = _tunnelAddress;
        }
    }

    public void UpdateFromMetadata(InstanceMetadata newMeta)
    {
        _metadata = newMeta;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(MinecraftVersion));
        OnPropertyChanged(nameof(ServerType));
        OnPropertyChanged(nameof(MaxPlayers));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    protected bool SetProperty<T>(ref T field, T value, string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName ?? string.Empty);
        return true;
    }
}
