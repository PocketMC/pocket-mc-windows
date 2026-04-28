using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Players.Services;
using PocketMC.Desktop.Helpers;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Players;

public sealed class PlayerManagementViewModel : ViewModelBase, IDisposable
{
    private readonly IAppNavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IAppDispatcher _dispatcher;
    private readonly ServerStateFileService _stateFileService;
    private readonly BanSidecarService _banSidecarService;
    private readonly InstanceMetadata _metadata;
    private readonly ServerProcess _serverProcess;
    private readonly ILogger<PlayerManagementViewModel> _logger;
    private readonly IDisposable _stateWatcher;
    private readonly DispatcherTimer _lastUpdatedTimer;
    private readonly SemaphoreSlim _stateRefreshLock = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _pendingGamemodePlayers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _oppedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _bannedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastUpdatedUtc;
    private bool _disposed;
    private bool _isRefreshingState;
    private string _lastUpdatedText = "Waiting for player list";

    public PlayerManagementViewModel(
        IAppNavigationService navigationService,
        IDialogService dialogService,
        IAppDispatcher dispatcher,
        ServerStateFileService stateFileService,
        BanSidecarService banSidecarService,
        InstanceMetadata metadata,
        ServerProcess serverProcess,
        ILogger<PlayerManagementViewModel> logger)
    {
        _navigationService = navigationService;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _stateFileService = stateFileService;
        _banSidecarService = banSidecarService;
        _metadata = metadata;
        _serverProcess = serverProcess;
        _logger = logger;

        BackCommand = new RelayCommand(_ => NavigateBack());
        RefreshCommand = new AsyncRelayCommand(_ => RequestPlayerListAsync());

        _serverProcess.OnOnlinePlayersUpdated += OnOnlinePlayersUpdated;
        _serverProcess.OnOutputLine += OnOutputLine;
        _serverProcess.OnStateChanged += OnServerStateChanged;
        _stateWatcher = _stateFileService.WatchForChanges(_metadata, () => _ = RefreshPersistentStateAsync());

        _lastUpdatedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lastUpdatedTimer.Tick += (_, _) => UpdateLastUpdatedText();
        _lastUpdatedTimer.Start();

        ApplyOnlinePlayers(_serverProcess.OnlinePlayerNames, _serverProcess.LastPlayerListUpdatedUtc);
        _ = RefreshPersistentStateAsync();
        _ = RequestPlayerListAsync();
    }

    public ObservableCollection<PlayerViewModel> OnlinePlayers { get; } = new();
    public ObservableCollection<BannedPlayerViewModel> BannedPlayers { get; } = new();
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }

    public string InstanceName => _metadata.Name;
    public string ServerType => _metadata.ServerType;
    public bool IsBedrock => CommandFormatter.IsBedrock(_metadata.ServerType);
    public bool IsPocketMine => CommandFormatter.IsPocketMine(_metadata.ServerType);
    public bool IsServerOnline => _serverProcess.State == ServerState.Online;
    public bool HasOnlinePlayers => OnlinePlayers.Count > 0;
    public bool HasBannedPlayers => BannedPlayers.Count > 0;

    public string EmptyOnlineText
    {
        get
        {
            if (!IsServerOnline)
            {
                return "Server is offline.";
            }

            return _lastUpdatedUtc.HasValue
                ? "No players online."
                : "Waiting for the next player list response.";
        }
    }

    public string EmptyBanText => IsBedrock
        ? "No Bedrock bans tracked by PocketMC."
        : "No banned players found in the server files.";

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string ServerStatusText => _serverProcess.State switch
    {
        ServerState.Online => "Online",
        ServerState.Installing => "Installing",
        ServerState.Starting => "Starting",
        ServerState.Stopping => "Stopping",
        ServerState.Crashed => "Crashed",
        _ => "Stopped"
    };

    public Brush ServerStatusBrush => _serverProcess.State switch
    {
        ServerState.Online => Brushes.LimeGreen,
        ServerState.Installing => Brushes.DeepSkyBlue,
        ServerState.Starting or ServerState.Stopping => Brushes.Orange,
        ServerState.Crashed => Brushes.Red,
        _ => Brushes.Gray
    };

    public bool IsRefreshingState
    {
        get => _isRefreshingState;
        private set => SetProperty(ref _isRefreshingState, value);
    }

    private async Task RequestPlayerListAsync()
    {
        if (!IsServerOnline)
        {
            return;
        }

        try
        {
            await _serverProcess.WriteInputAsync("list");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to request player list for instance {InstanceId}.", _metadata.Id);
        }
    }

    private void OnOnlinePlayersUpdated(IReadOnlyList<string> names, DateTime updatedAtUtc)
    {
        _ = _dispatcher.InvokeAsync(() => ApplyOnlinePlayers(names, updatedAtUtc));
    }

    private void ApplyOnlinePlayers(IReadOnlyList<string> names, DateTime? updatedAtUtc)
    {
        if (_disposed)
        {
            return;
        }

        _lastUpdatedUtc = updatedAtUtc;
        List<string> uniqueNames = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, PlayerViewModel> existing = OnlinePlayers
            .ToDictionary(player => player.Name, StringComparer.OrdinalIgnoreCase);

        OnlinePlayers.Clear();
        foreach (string name in uniqueNames)
        {
            if (!existing.TryGetValue(name, out PlayerViewModel? player))
            {
                player = new PlayerViewModel(
                    name,
                    ToggleOpAsync,
                    ChangeGamemodeAsync,
                    SubmitReasonAsync);
            }

            player.IsServerOnline = IsServerOnline;
            player.SetOpFromState(_oppedPlayers.Contains(name));
            player.IsBanned = _bannedPlayers.Contains(name);
            OnlinePlayers.Add(player);
        }

        UpdateLastUpdatedText();
        OnPropertyChanged(nameof(HasOnlinePlayers));
        OnPropertyChanged(nameof(EmptyOnlineText));
    }

    private async Task RefreshPersistentStateAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateRefreshLock.WaitAsync();
        try
        {
            await _dispatcher.InvokeAsync(() => IsRefreshingState = true);

            List<string> oppedPlayers = await _stateFileService.GetOppedPlayersAsync(_metadata);
            List<BannedPlayerEntry> bans = IsBedrock
                ? await _banSidecarService.GetBannedPlayersAsync(_metadata)
                : await _stateFileService.GetBannedPlayersAsync(_metadata);

            await _dispatcher.InvokeAsync(() =>
            {
                _oppedPlayers = oppedPlayers.ToHashSet(StringComparer.OrdinalIgnoreCase);
                _bannedPlayers = bans.Select(ban => ban.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (PlayerViewModel player in OnlinePlayers)
                {
                    player.SetOpFromState(_oppedPlayers.Contains(player.Name));
                    player.IsBanned = _bannedPlayers.Contains(player.Name);
                    player.IsServerOnline = IsServerOnline;
                }

                SyncBanList(bans);
                IsRefreshingState = false;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh player state for instance {InstanceId}.", _metadata.Id);
            await _dispatcher.InvokeAsync(() => IsRefreshingState = false);
        }
        finally
        {
            _stateRefreshLock.Release();
        }
    }

    private void SyncBanList(IReadOnlyList<BannedPlayerEntry> bans)
    {
        BannedPlayers.Clear();
        foreach (BannedPlayerEntry ban in bans.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            BannedPlayers.Add(new BannedPlayerViewModel(
                ban.Name,
                ban.Reason,
                ban.Created,
                ban.Expires,
                ban.IsSidecar,
                IsServerOnline,
                PardonAsync));
        }

        OnPropertyChanged(nameof(HasBannedPlayers));
        OnPropertyChanged(nameof(EmptyBanText));
    }

    private async Task ToggleOpAsync(PlayerViewModel player)
    {
        if (!IsServerOnline)
        {
            return;
        }

        bool targetState = !player.IsOp;
        player.IsOpUpdating = true;
        player.RefreshOpBinding();

        string command = targetState ? "op" : "deop";
        await DispatchCommandAsync($"{command} {CommandFormatter.FormatPlayerName(player.Name, _metadata.ServerType)}");
        _ = RefreshOpAfterDelayAsync(player);
    }

    private async Task RefreshOpAfterDelayAsync(PlayerViewModel player)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (!player.IsOpUpdating)
        {
            return;
        }

        _logger.LogWarning(
            "Operator state did not update within 3 seconds for player {PlayerName} on instance {InstanceId}. Refreshing state files.",
            player.Name,
            _metadata.Id);
        await RefreshPersistentStateAsync();
    }

    private async Task ChangeGamemodeAsync(PlayerViewModel player, string mode)
    {
        if (!IsServerOnline)
        {
            return;
        }

        _pendingGamemodePlayers[player.Name] = DateTime.UtcNow;
        await DispatchCommandAsync($"gamemode {mode} {CommandFormatter.FormatPlayerName(player.Name, _metadata.ServerType)}");
    }

    private async Task<bool> SubmitReasonAsync(PlayerViewModel player, string action, string reason)
    {
        if (!IsServerOnline)
        {
            return false;
        }

        if (string.Equals(action, "Ban", StringComparison.OrdinalIgnoreCase))
        {
            DialogResult result = await _dialogService.ShowDialogAsync(
                "Ban Player",
                $"Ban {player.Name} from {InstanceName}?",
                DialogType.Warning,
                true);

            if (result != DialogResult.Yes)
            {
                return false;
            }
        }

        string formattedName = CommandFormatter.FormatPlayerName(player.Name, _metadata.ServerType);
        string sanitizedReason = CommandFormatter.SanitizeReason(reason);
        string commandName = action.Equals("Kick", StringComparison.OrdinalIgnoreCase) ? "kick" : "ban";
        string command = CommandFormatter.AppendOptionalReason($"{commandName} {formattedName}", sanitizedReason);
        await DispatchCommandAsync(command);

        if (IsBedrock && commandName == "ban")
        {
            await _banSidecarService.AddBanAsync(_metadata, player.Name, sanitizedReason);
            await RefreshPersistentStateAsync();
        }

        return true;
    }

    private async Task PardonAsync(BannedPlayerViewModel bannedPlayer)
    {
        if (!IsServerOnline)
        {
            return;
        }

        await DispatchCommandAsync($"pardon {CommandFormatter.FormatPlayerName(bannedPlayer.Name, _metadata.ServerType)}");
        if (IsBedrock || bannedPlayer.IsSidecar)
        {
            await _banSidecarService.RemoveBanAsync(_metadata, bannedPlayer.Name);
            await RefreshPersistentStateAsync();
        }
    }

    private async Task DispatchCommandAsync(string command)
    {
        try
        {
            await _serverProcess.WriteInputAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch command '{Command}' for instance {InstanceId}.", command, _metadata.Id);
            _dialogService.ShowMessage("Command Failed", ex.Message, DialogType.Error);
        }
    }

    private void OnOutputLine(string line)
    {
        if (!line.Contains("game mode", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string playerName in _pendingGamemodePlayers.Keys.ToArray())
        {
            if (!line.Contains(playerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _pendingGamemodePlayers.TryRemove(playerName, out _);
            _ = _dispatcher.InvokeAsync(async () =>
            {
                PlayerViewModel? player = OnlinePlayers.FirstOrDefault(p =>
                    string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase));
                if (player != null)
                {
                    await player.FlashSuccessAsync();
                }
            });
        }
    }

    private void OnServerStateChanged(ServerState state)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(IsServerOnline));
            OnPropertyChanged(nameof(ServerStatusText));
            OnPropertyChanged(nameof(ServerStatusBrush));
            OnPropertyChanged(nameof(EmptyOnlineText));
            foreach (PlayerViewModel player in OnlinePlayers)
            {
                player.IsServerOnline = IsServerOnline;
            }

            foreach (BannedPlayerViewModel bannedPlayer in BannedPlayers)
            {
                bannedPlayer.IsServerOnline = IsServerOnline;
            }
        });
    }

    private void UpdateLastUpdatedText()
    {
        if (!_lastUpdatedUtc.HasValue)
        {
            LastUpdatedText = "Waiting for player list";
            return;
        }

        int seconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - _lastUpdatedUtc.Value).TotalSeconds));
        LastUpdatedText = seconds <= 1
            ? "Last updated just now"
            : $"Last updated {seconds}s ago";
    }

    private void NavigateBack()
    {
        if (!_navigationService.NavigateBack())
        {
            _navigationService.NavigateToDashboard();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lastUpdatedTimer.Stop();
        _stateWatcher.Dispose();
        _stateRefreshLock.Dispose();
        _serverProcess.OnOnlinePlayersUpdated -= OnOnlinePlayersUpdated;
        _serverProcess.OnOutputLine -= OnOutputLine;
        _serverProcess.OnStateChanged -= OnServerStateChanged;
    }
}
