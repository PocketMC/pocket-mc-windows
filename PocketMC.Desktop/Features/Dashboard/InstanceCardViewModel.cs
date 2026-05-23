using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Features.Dashboard;

public class InstanceCardViewModel : INotifyPropertyChanged
{
    private InstanceMetadata _metadata;
    private readonly ServerProcessManager _serverProcessManager;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly PocketMC.Desktop.Features.Shell.ApplicationState _appState;
    private ServerState _state = ServerState.Stopped;
    private string? _countdownText;
    private string _cpuText = "· · ·";
    private string _ramText = "· · ·";
    private string _playerStatus = "· · ·";
    private string? _tunnelAddress;
    private string? _numericTunnelAddress;
    private string? _bedrockTunnelAddress;
    private string? _bedrockNumericTunnelAddress;
    private string? _voiceChatTunnelAddress;
    private string? _voiceChatNumericTunnelAddress;
    private string? _simpleVoiceChatWarning;
    private string? _bedrockIpDisplayTextOverride;
    private int _bedrockLocalPort;
    private string _ipDisplayText = "Will Appear Here!";
    private string? _portIssueText;
    private string? _portIssueTooltip;
    private bool _isTunnelResolving;

    private readonly InstanceRegistry _registry;

    public InstanceCardViewModel(InstanceMetadata metadata, ServerProcessManager serverProcessManager, IServerLifecycleService lifecycleService, PocketMC.Desktop.Features.Shell.ApplicationState appState, InstanceRegistry registry)
    {
        _metadata = metadata;
        _serverProcessManager = serverProcessManager;
        _lifecycleService = lifecycleService;
        _appState = appState;
        _registry = registry;
        _bedrockLocalPort = metadata.GeyserBedrockPort ?? 19132;

        _tunnelAddress = appState.GetTunnelAddress(metadata.Id);
        _numericTunnelAddress = appState.GetNumericTunnelAddress(metadata.Id);
        _bedrockTunnelAddress = appState.GetBedrockTunnelAddress(metadata.Id);
        _bedrockNumericTunnelAddress = appState.GetBedrockNumericTunnelAddress(metadata.Id);
        _voiceChatTunnelAddress = appState.GetVoiceChatTunnelAddress(metadata.Id);
        _voiceChatNumericTunnelAddress = appState.GetVoiceChatNumericTunnelAddress(metadata.Id);
        _simpleVoiceChatWarning = metadata.SimpleVoiceChatLastWarning;

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
    public bool IsRunning => _state == ServerState.Installing || _state == ServerState.SettingUp || _state == ServerState.Starting || _state == ServerState.Online || _state == ServerState.Stopping;
    public bool IsWaitingToRestart => _lifecycleService.IsWaitingToRestart(Id);
    public bool ShowRunningControls => IsRunning || IsWaitingToRestart;
    public Visibility RunningControlsVisibility => ShowRunningControls ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StoppedControlsVisibility => ShowRunningControls ? Visibility.Collapsed : Visibility.Visible;
    public string StopButtonText => IsWaitingToRestart ? "Abort" : "Stop";
    public string MinecraftVersion => _metadata.MinecraftVersion;
    public string ServerType => _metadata.ServerType;
    public int MaxPlayers => _metadata.MaxPlayers;
    public string LastPlayedText => _metadata.LastPlayedAt.HasValue
        ? $"Last played: {_metadata.LastPlayedAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)}"
        : "Last played: Never";
    public string LastPlayedValueText => _metadata.LastPlayedAt.HasValue
        ? FormatRelativeTime(_metadata.LastPlayedAt.Value)
        : "Never";
    public string LastPlayedTooltip => _metadata.LastPlayedAt.HasValue
        ? _metadata.LastPlayedAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)
        : "Never played";
    public string CreatedText => $"Created: {_metadata.CreatedAt.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";
    public string CreatedValueText => _metadata.CreatedAt.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    public bool ShowCrossPlayBadge => HasGeyser;
    public Visibility CrossPlayBadgeVisibility => ShowCrossPlayBadge ? Visibility.Visible : Visibility.Collapsed;
    public string CrossPlayBadgeText => "Cross-play";
    public string CrossPlayBadgeTooltip => "Java and Bedrock players can join through Geyser/Floodgate.";

    public bool ShowVoiceChatBadge
    {
        get
        {
            try
            {
                string? path = _registry.GetPath(Id);
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
                var detection = SimpleVoiceChatDetector.Detect(path);
                return detection.IsDetected;
            }
            catch
            {
                return false;
            }
        }
    }
    public Visibility VoiceChatBadgeVisibility => ShowVoiceChatBadge ? Visibility.Visible : Visibility.Collapsed;
    public string VoiceChatBadgeText => "Voice Chat";
    public string VoiceChatBadgeTooltip => "Simple Voice Chat is active on this server.";

    public string PlatformSummaryText => ShowCrossPlayBadge
        ? "Java + Bedrock"
        : IsBedrockServer ? "Bedrock network" : "Java network";
    public bool HasTunnelAddress => !string.IsNullOrEmpty(_tunnelAddress);
    public bool HasBedrockTunnelAddress => !string.IsNullOrEmpty(_bedrockTunnelAddress);
    public bool HasVoiceChatTunnelAddress => !string.IsNullOrEmpty(_voiceChatTunnelAddress);
    public bool HasSimpleVoiceChatTunnelAddress => HasVoiceChatTunnelAddress;
    public bool HasSimpleVoiceChatWarning => !string.IsNullOrWhiteSpace(_simpleVoiceChatWarning);
    public string? SimpleVoiceChatWarning => _simpleVoiceChatWarning;
    public Visibility SimpleVoiceChatWarningVisibility => HasSimpleVoiceChatWarning ? Visibility.Visible : Visibility.Collapsed;
    public bool HasPortIssue => !string.IsNullOrWhiteSpace(_portIssueText);
    public Visibility PortIssueVisibility => HasPortIssue ? Visibility.Visible : Visibility.Collapsed;
    public string? PortIssueText => _portIssueText;
    public string? PortIssueTooltip => _portIssueTooltip;

    /// <summary>True while the tunnel connect address is being resolved.</summary>
    public bool IsTunnelResolving
    {
        get => _isTunnelResolving;
        set
        {
            if (SetProperty(ref _isTunnelResolving, value))
            {
                OnPropertyChanged(nameof(ShowPrimaryTunnelSkeleton));
                OnPropertyChanged(nameof(ShowBedrockNumericSkeleton));
                OnPropertyChanged(nameof(ShowGeyserHostnameSkeleton));
                OnPropertyChanged(nameof(ShowGeyserNumericSkeleton));
            }
        }
    }


    /// <summary>True when the primary tunnel address skeleton should show
    /// (resolving and primary hostname/address not yet received).</summary>
    public bool ShowPrimaryTunnelSkeleton => _isTunnelResolving && !HasTunnelAddress;

    /// <summary>True when the native-Bedrock numeric IP skeleton should show.</summary>
    public bool ShowBedrockNumericSkeleton => IsBedrockServer && (!HasNumericTunnelAddress && (_isTunnelResolving || HasTunnelAddress));

    /// <summary>True when the Geyser Bedrock hostname skeleton should show.</summary>
    public bool ShowGeyserHostnameSkeleton => _isTunnelResolving && HasGeyser && !HasBedrockTunnelAddress;

    /// <summary>True when the Geyser Bedrock numeric skeleton should show.</summary>
    public bool ShowGeyserNumericSkeleton => HasGeyser && (!HasBedrockNumericTunnelAddress && (_isTunnelResolving || HasBedrockTunnelAddress));

    public void SetTunnelResolving(bool resolving)
    {
        IsTunnelResolving = resolving;
    }

    /// <summary>True for native Bedrock servers (BDS, Pocketmine).</summary>
    public bool IsBedrockServer =>
        _metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true ||
        _metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when a Java server has Geyser cross-play enabled.</summary>
    public bool HasGeyser
    {
        get
        {
            if (!_metadata.HasGeyser) return false;
            try
            {
                string? path = _registry.GetPath(Id);
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;

                string pluginsDir = Path.Combine(path, "plugins");
                string modsDir = Path.Combine(path, "mods");

                bool geyserInPlugins = Directory.Exists(pluginsDir) && 
                                       Directory.EnumerateFiles(pluginsDir, "Geyser*.jar", SearchOption.TopDirectoryOnly).Any();

                bool geyserInMods = Directory.Exists(modsDir) && 
                                     Directory.EnumerateFiles(modsDir, "Geyser*.jar", SearchOption.TopDirectoryOnly).Any();

                return geyserInPlugins || geyserInMods;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Whether to show a separate Bedrock IP row on the card.
    /// Only meaningful for Java servers with Geyser cross-play enabled, where
    /// Java players and Bedrock players reach the server on different addresses.
    /// Native BDS / PocketMine servers have a single address — no second row needed.</summary>
    public bool ShowBedrockIp => !IsBedrockServer && HasGeyser;

    /// <summary>Label prefix for the secondary IP row.</summary>
    public string BedrockIpLabel => "Bedrock (Geyser):";

    public int PrimaryPort => _metadata.ServerPort ?? (IsBedrockServer ? 19132 : 25565);
    public string LanAddressDisplayText => $"127.0.0.1:{PrimaryPort}";
    public bool HasLanAddress => IsRunning;

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
        ServerState.Installing => "⚙ Installing...",
        ServerState.SettingUp => "⚙ Setting Up...",
        ServerState.Starting => "● Starting",
        ServerState.Online => "● Online",
        ServerState.Stopping => "● Stopping",
        ServerState.Crashed => "⚠️ Crashed",
        _ => "● Unknown"
    };

    public Brush StatusBrush => _state switch
    {
        ServerState.Online => Brushes.LimeGreen,
        ServerState.Installing => Brushes.DeepSkyBlue,
        ServerState.SettingUp => Brushes.DeepSkyBlue,
        ServerState.Starting => Brushes.SkyBlue,
        ServerState.Stopping => Brushes.Orange,
        ServerState.Crashed => Brushes.Red,
        _ => Brushes.DarkGray
    };

    public string CpuText { get => _cpuText; set { if (_cpuText != value) { _cpuText = value; OnPropertyChanged(nameof(CpuText)); } } }
    public string RamText { get => _ramText; set { if (_ramText != value) { _ramText = value; OnPropertyChanged(nameof(RamText)); } } }
    public string PlayerStatus { get => _playerStatus; set { if (_playerStatus != value) { _playerStatus = value; OnPropertyChanged(nameof(PlayerStatus)); } } }
    public string IpDisplayText { get => _ipDisplayText; set { if (_ipDisplayText != value) { _ipDisplayText = value; OnPropertyChanged(nameof(IpDisplayText)); } } }

    public string? NumericTunnelAddress
    {
        get => _numericTunnelAddress;
        set
        {
            if (SetProperty(ref _numericTunnelAddress, value))
            {
                OnPropertyChanged(nameof(HasNumericTunnelAddress));
                OnPropertyChanged(nameof(ShowBedrockNumericSkeleton));
            }
        }
    }
    public bool HasNumericTunnelAddress => !string.IsNullOrEmpty(_numericTunnelAddress);

    public string? BedrockNumericTunnelAddress
    {
        get => _bedrockNumericTunnelAddress;
        set
        {
            if (SetProperty(ref _bedrockNumericTunnelAddress, value))
            {
                OnPropertyChanged(nameof(HasBedrockNumericTunnelAddress));
                OnPropertyChanged(nameof(ShowGeyserNumericSkeleton));
            }
        }
    }
    public bool HasBedrockNumericTunnelAddress => !string.IsNullOrEmpty(_bedrockNumericTunnelAddress);

    public string? VoiceChatNumericTunnelAddress
    {
        get => _voiceChatNumericTunnelAddress;
        set
        {
            if (SetProperty(ref _voiceChatNumericTunnelAddress, value))
            {
                OnPropertyChanged(nameof(HasVoiceChatNumericTunnelAddress));
                OnPropertyChanged(nameof(HasSimpleVoiceChatNumericTunnelAddress));
            }
        }
    }
    public bool HasVoiceChatNumericTunnelAddress => !string.IsNullOrEmpty(_voiceChatNumericTunnelAddress);
    public bool HasSimpleVoiceChatNumericTunnelAddress => HasVoiceChatNumericTunnelAddress;

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
                OnPropertyChanged(nameof(ShowPrimaryTunnelSkeleton));
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
                OnPropertyChanged(nameof(HasBedrockTunnelAddress));
                OnPropertyChanged(nameof(BedrockIpDisplayText));
                OnPropertyChanged(nameof(ShowGeyserHostnameSkeleton));
            }
        }
    }

    public string? VoiceChatTunnelAddress
    {
        get => _voiceChatTunnelAddress;
        set
        {
            if (SetProperty(ref _voiceChatTunnelAddress, value))
            {
                OnPropertyChanged(nameof(HasVoiceChatTunnelAddress));
                OnPropertyChanged(nameof(HasSimpleVoiceChatTunnelAddress));
                OnPropertyChanged(nameof(SimpleVoiceChatTunnelAddress));
            }
        }
    }

    public string? SimpleVoiceChatTunnelAddress
    {
        get => VoiceChatTunnelAddress;
        set => VoiceChatTunnelAddress = value;
    }

    public void SetSimpleVoiceChatWarning(string warning)
    {
        _simpleVoiceChatWarning = warning;
        OnPropertyChanged(nameof(HasSimpleVoiceChatWarning));
        OnPropertyChanged(nameof(SimpleVoiceChatWarning));
        OnPropertyChanged(nameof(SimpleVoiceChatWarningVisibility));
    }

    public void ClearSimpleVoiceChatWarning()
    {
        if (_simpleVoiceChatWarning == null)
        {
            return;
        }

        _simpleVoiceChatWarning = null;
        OnPropertyChanged(nameof(HasSimpleVoiceChatWarning));
        OnPropertyChanged(nameof(SimpleVoiceChatWarning));
        OnPropertyChanged(nameof(SimpleVoiceChatWarningVisibility));
    }

    public string DisplayAddress => _tunnelAddress ?? "127.0.0.1";

    public void UpdateState(ServerState newState)
    {
        if (_state != newState)
        {
            if (newState == ServerState.SettingUp || newState == ServerState.Starting)
            {
                TunnelAddress = null;
                NumericTunnelAddress = null;
                BedrockTunnelAddress = null;
                BedrockNumericTunnelAddress = null;
                VoiceChatTunnelAddress = null;
                VoiceChatNumericTunnelAddress = null;
                _appState.ClearTunnelAddress(Id);
            }

            _state = newState;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(RunningControlsVisibility));
            OnPropertyChanged(nameof(StoppedControlsVisibility));
            OnPropertyChanged(nameof(StopButtonText));
            OnPropertyChanged(nameof(HasLanAddress));
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
        OnPropertyChanged(nameof(HasGeyser));
        OnPropertyChanged(nameof(IsBedrockServer));
        OnPropertyChanged(nameof(ShowBedrockIp));
        OnPropertyChanged(nameof(ShowCrossPlayBadge));
        OnPropertyChanged(nameof(CrossPlayBadgeVisibility));
        OnPropertyChanged(nameof(CrossPlayBadgeText));
        OnPropertyChanged(nameof(CrossPlayBadgeTooltip));
        OnPropertyChanged(nameof(ShowVoiceChatBadge));
        OnPropertyChanged(nameof(VoiceChatBadgeVisibility));
        OnPropertyChanged(nameof(VoiceChatBadgeText));
        OnPropertyChanged(nameof(VoiceChatBadgeTooltip));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(LastPlayedValueText));
        OnPropertyChanged(nameof(LastPlayedTooltip));
        OnPropertyChanged(nameof(CreatedText));
        OnPropertyChanged(nameof(CreatedValueText));
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(PrimaryPort));
        OnPropertyChanged(nameof(LanAddressDisplayText));
        OnPropertyChanged(nameof(BedrockIpDisplayText));
    }

    /// <summary>
    /// Converts a UTC DateTime to a human-friendly relative time string.
    /// Examples: "Just now", "5 min ago", "3 hours ago", "2 days ago",
    /// "3 weeks ago", "2 months ago", "1 year ago".
    /// </summary>
    internal static string FormatRelativeTime(DateTime utcDateTime)
    {
        var elapsed = DateTime.UtcNow - utcDateTime;

        if (elapsed.TotalSeconds < 60)
            return "Just now";
        if (elapsed.TotalMinutes < 60)
        {
            int minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 min ago" : $"{minutes} min ago";
        }
        if (elapsed.TotalHours < 24)
        {
            int hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        if (elapsed.TotalDays < 7)
        {
            int days = (int)elapsed.TotalDays;
            return days == 1 ? "Yesterday" : $"{days} days ago";
        }
        if (elapsed.TotalDays < 30)
        {
            int weeks = (int)(elapsed.TotalDays / 7);
            return weeks == 1 ? "1 week ago" : $"{weeks} weeks ago";
        }
        if (elapsed.TotalDays < 365)
        {
            int months = (int)(elapsed.TotalDays / 30);
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        int years = (int)(elapsed.TotalDays / 365);
        return years == 1 ? "1 year ago" : $"{years} years ago";
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
