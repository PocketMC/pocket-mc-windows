using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
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
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Helpers;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Players;

public sealed class PlayerManagementViewModel : ViewModelBase, IDisposable
{
    private static readonly Regex BedrockConnectionRegex = new(
        @"Player\s+connected:\s*(?<name>.+?),\s*xuid:\s*(?<xuid>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex BedrockGamemodeRegex = new(
        @"Game\s+mode\s+of\s+(?<name>.+?)\s+has\s+been\s+updated\s+to\s+(?<mode>Survival|Creative|Adventure|Spectator)\s+Mode",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly IAppNavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IAppDispatcher _dispatcher;
    private readonly ServerStateFileService _stateFileService;
    private readonly BanSidecarService _banSidecarService;
    private readonly InstanceMetadata _metadata;
    private readonly ServerProcess _serverProcess;
    private readonly ILogger<PlayerManagementViewModel> _logger;
    private readonly PlayerDataService _playerDataService;
    private readonly BedrockPlayerDataService _bedrockPlayerDataService;
    private readonly IDisposable _stateWatcher;
    private readonly IDisposable _playerDataWatcher;
    private readonly IDisposable _bedrockPermissionsWatcher;
    private readonly DispatcherTimer _lastUpdatedTimer;
    private readonly SemaphoreSlim _stateRefreshLock = new(1, 1);
    private readonly ConcurrentDictionary<string, PendingGamemodeChange> _pendingGamemodePlayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _playerUuidByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _pendingBedrockOpSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _oppedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _bannedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownBedrockPlayers = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastUpdatedUtc;
    private bool _disposed;
    private bool _hasLoadedOpState;
    private bool _isRefreshingState;
    private string _lastUpdatedText = "Waiting for player list";

    public PlayerManagementViewModel(
        IAppNavigationService navigationService,
        IDialogService dialogService,
        IAppDispatcher dispatcher,
        ServerStateFileService stateFileService,
        BanSidecarService banSidecarService,
        ApplicationState applicationState,
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
        _playerDataService = new PlayerDataService(_serverProcess.WorkingDirectory);
        _bedrockPlayerDataService = new BedrockPlayerDataService(
            applicationState.GetRequiredAppRootPath(),
            _serverProcess.WorkingDirectory);

        BackCommand = new RelayCommand(_ => NavigateBack());
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAllPlayerDataAsync());

        _serverProcess.OnOnlinePlayersUpdated += OnOnlinePlayersUpdated;
        _serverProcess.OnOutputLine += OnOutputLine;
        _serverProcess.OnStateChanged += OnServerStateChanged;
        _stateWatcher = IsBedrock
            ? EmptyDisposable.Instance
            : _stateFileService.WatchForChanges(_metadata, () => _ = RefreshPersistentStateAsync());
        _playerDataWatcher = UsesJavaNativePlayerData
            ? _playerDataService.WatchForChanges(_opsPath => { _ = RefreshPersistentStateAsync(); }, OnPlayerdataChanged)
            : EmptyDisposable.Instance;
        _bedrockPermissionsWatcher = IsBedrock
            ? _bedrockPlayerDataService.WatchPermissionsFile(() => _ = RefreshPersistentStateAsync())
            : EmptyDisposable.Instance;

        _lastUpdatedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lastUpdatedTimer.Tick += (_, _) => UpdateLastUpdatedText();
        _lastUpdatedTimer.Start();

        _ = ImportBedrockPlayerMapFromOutputBufferAsync();
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
    private bool UsesJavaNativePlayerData => !IsBedrock && !IsPocketMine;
    private bool UsesSidecarGamemode => IsBedrock || IsPocketMine;
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
            await _serverProcess.WriteListCommandAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to request player list for instance {InstanceId}.", _metadata.Id);
        }
    }

    /// <summary>
    /// Full refresh triggered by the user's Refresh button.
    /// Re-requests the player list, refreshes OP status from authoritative files,
    /// and re-fetches the latest gamemode for every online player.
    /// Works across all engine types (Java, Bedrock, PocketMine).
    /// </summary>
    private async Task RefreshAllPlayerDataAsync()
    {
        // 1. Request the updated player list from the server
        await RequestPlayerListAsync();

        // 2. Refresh persistent state (OP, bans) from authoritative files
        await RefreshPersistentStateAsync();

        // 3. Re-fetch per-player gamemode from the latest source
        List<string> onlineNames = new();
        await _dispatcher.InvokeAsync(() =>
        {
            onlineNames = OnlinePlayers.Select(player => player.Name).ToList();
            foreach (PlayerViewModel player in OnlinePlayers)
            {
                player.IsGamemodeLoading = true;
            }
        });

        foreach (string name in onlineNames)
        {
            if (_disposed)
            {
                return;
            }

            if (IsBedrock)
            {
                await LoadBedrockPlayerStateAsync(name);
            }
            else if (UsesJavaNativePlayerData)
            {
                await LoadJavaGamemodeAsync(name);
            }
            else if (UsesSidecarGamemode)
            {
                await LoadSidecarGamemodeAsync(name);
            }
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
            .Select(PlayerListParser.NormalizePlayerName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
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
            if (IsBedrock)
            {
                if (_hasLoadedOpState && _knownBedrockPlayers.Contains(name))
                {
                    player.SetOpFromState(_oppedPlayers.Contains(name));
                }
                else
                {
                    player.IsOpLoading = true;
                }

                player.IsGamemodeLoading = false;
                _ = LoadBedrockPlayerStateAsync(name);
            }
            else if (_hasLoadedOpState || !UsesJavaNativePlayerData)
            {
                player.SetOpFromState(_oppedPlayers.Contains(name));
            }
            else
            {
                player.IsOpLoading = true;
            }

            if (UsesJavaNativePlayerData)
            {
                if (!_playerUuidByName.ContainsKey(name) || player.IsGamemodeLoading)
                {
                    player.IsGamemodeLoading = true;
                    _ = LoadJavaGamemodeAsync(name);
                }
            }
            else if (UsesSidecarGamemode && !IsBedrock)
            {
                player.IsGamemodeLoading = false;
                _ = LoadSidecarGamemodeAsync(name);
            }
            else
            {
                player.IsGamemodeLoading = false;
            }

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

            if (IsBedrock)
            {
                await ImportBedrockPlayerMapFromOutputBufferAsync();
                await ResolvePendingBedrockOpMappingsAsync();
            }

            List<string> onlineNames = new();
            await _dispatcher.InvokeAsync(() =>
            {
                onlineNames = OnlinePlayers.Select(player => player.Name).ToList();
            });

            Dictionary<string, bool> knownBedrockPlayers = new(StringComparer.OrdinalIgnoreCase);
            if (IsBedrock)
            {
                foreach (string name in onlineNames)
                {
                    knownBedrockPlayers[name] = await _bedrockPlayerDataService.GetXuidAsync(name) != null;
                }
            }

            HashSet<string> oppedPlayers = IsBedrock
                ? await _bedrockPlayerDataService.GetOppedPlayerNamesAsync()
                : UsesJavaNativePlayerData
                ? await _playerDataService.GetOppedPlayersAsync()
                : (await _stateFileService.GetOppedPlayersAsync(_metadata)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<BannedPlayerEntry> bans = await GetBannedPlayersAsync();

            await _dispatcher.InvokeAsync(() =>
            {
                _oppedPlayers = oppedPlayers;
                _hasLoadedOpState = true;
                _bannedPlayers = bans.Select(ban => ban.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (IsBedrock)
                {
                    _knownBedrockPlayers = knownBedrockPlayers
                        .Where(pair => pair.Value)
                        .Select(pair => pair.Key)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }

                foreach (PlayerViewModel player in OnlinePlayers)
                {
                    if (IsBedrock &&
                        knownBedrockPlayers.TryGetValue(player.Name, out bool isKnownBedrockPlayer) &&
                        !isKnownBedrockPlayer)
                    {
                        player.SetOpFromState(false);
                    }
                    else
                    {
                        player.SetOpFromState(_oppedPlayers.Contains(player.Name));
                    }

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

    private async Task<List<BannedPlayerEntry>> GetBannedPlayersAsync()
    {
        if (IsBedrock)
        {
            List<BedrockBanEntry> bans = await _bedrockPlayerDataService.GetBansAsync();
            return bans.Select(ban => new BannedPlayerEntry
            {
                Name = ban.Name,
                Reason = ban.Reason,
                Created = ban.BannedAt.ToUniversalTime().ToString("O"),
                Expires = "forever",
                IsSidecar = true
            }).ToList();
        }

        return await _stateFileService.GetBannedPlayersAsync(_metadata);
    }

    private async Task LoadJavaGamemodeAsync(string playerName)
    {
        if (_disposed || !UsesJavaNativePlayerData)
        {
            return;
        }

        try
        {
            string? uuid = await _playerDataService.GetUuidAsync(playerName);
            if (uuid == null)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    PlayerViewModel? player = FindOnlinePlayer(playerName);
                    if (player != null)
                    {
                        player.SetGameModeSilently("survival");
                        player.IsGamemodeLoading = true;
                    }
                });
                return;
            }

            _playerUuidByName[playerName] = uuid;
            string gamemode = await _playerDataService.GetGamemodeAsync(uuid);

            await _dispatcher.InvokeAsync(() =>
            {
                PlayerViewModel? player = FindOnlinePlayer(playerName);
                player?.SetGameModeFromServer(gamemode);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Java playerdata for {PlayerName} on instance {InstanceId}.", playerName, _metadata.Id);
            await _dispatcher.InvokeAsync(() =>
            {
                PlayerViewModel? player = FindOnlinePlayer(playerName);
                if (player != null)
                {
                    player.SetGameModeSilently("survival");
                    player.IsGamemodeLoading = false;
                }
            });
        }
    }

    private async Task LoadBedrockPlayerStateAsync(string playerName)
    {
        if (_disposed || !IsBedrock)
        {
            return;
        }

        try
        {
            string? xuid = await _bedrockPlayerDataService.GetXuidAsync(playerName);
            HashSet<string> oppedPlayers = await _bedrockPlayerDataService.GetOppedPlayerNamesAsync();
            string gamemode = await _bedrockPlayerDataService.GetGamemodeAsync(playerName);

            await _dispatcher.InvokeAsync(() =>
            {
                PlayerViewModel? player = FindOnlinePlayer(playerName);
                if (player == null)
                {
                    return;
                }

                if (xuid == null)
                {
                    player.SetOpFromState(false);
                }
                else
                {
                    _knownBedrockPlayers.Add(playerName);
                    player.SetOpFromState(oppedPlayers.Contains(playerName));
                }

                player.SetGameModeFromServer(gamemode);
            });

            BedrockBanEntry? ban = await _bedrockPlayerDataService.GetBanForPlayerAsync(playerName, xuid);
            if (ban != null && IsServerOnline)
            {
                await KickBedrockBannedPlayerAsync(playerName, ban);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Bedrock player data for {PlayerName} on instance {InstanceId}.", playerName, _metadata.Id);
        }
    }

    private async Task LoadSidecarGamemodeAsync(string playerName)
    {
        if (_disposed || !UsesSidecarGamemode)
        {
            return;
        }

        try
        {
            string gamemode = await _bedrockPlayerDataService.GetGamemodeAsync(playerName);
            await _dispatcher.InvokeAsync(() =>
            {
                PlayerViewModel? player = FindOnlinePlayer(playerName);
                player?.SetGameModeFromServer(gamemode);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load sidecar gamemode for {PlayerName} on instance {InstanceId}.", playerName, _metadata.Id);
        }
    }

    private void OnPlayerdataChanged(string uuid)
    {
        _ = RefreshChangedPlayerdataAsync(uuid);
    }

    private async Task RefreshChangedPlayerdataAsync(string uuid)
    {
        if (_disposed || !UsesJavaNativePlayerData)
        {
            return;
        }

        try
        {
            List<string> onlineNames = new();
            await _dispatcher.InvokeAsync(() =>
            {
                onlineNames = OnlinePlayers.Select(player => player.Name).ToList();
            });

            string? playerName = _playerUuidByName
                .FirstOrDefault(pair => string.Equals(pair.Value, uuid, StringComparison.OrdinalIgnoreCase))
                .Key;

            if (string.IsNullOrWhiteSpace(playerName))
            {
                foreach (string name in onlineNames)
                {
                    string? cachedUuid = await _playerDataService.GetUuidAsync(name);
                    if (cachedUuid == null)
                    {
                        continue;
                    }

                    _playerUuidByName[name] = cachedUuid;
                    if (string.Equals(cachedUuid, uuid, StringComparison.OrdinalIgnoreCase))
                    {
                        playerName = name;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                return;
            }

            string gamemode = await _playerDataService.GetGamemodeAsync(uuid);
            await _dispatcher.InvokeAsync(() =>
            {
                PlayerViewModel? player = FindOnlinePlayer(playerName);
                player?.SetGameModeFromServer(gamemode);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh Java playerdata change for UUID {Uuid} on instance {InstanceId}.", uuid, _metadata.Id);
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

    private async Task ImportBedrockPlayerMapFromOutputBufferAsync()
    {
        if (!IsBedrock)
        {
            return;
        }

        try
        {
            await _bedrockPlayerDataService.ImportPlayerMapFromLogLinesAsync(_serverProcess.OutputBuffer.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to backfill Bedrock player XUID map from console output for instance {InstanceId}.", _metadata.Id);
        }
    }

    private async Task ResolvePendingBedrockOpMappingsAsync()
    {
        if (!IsBedrock || _pendingBedrockOpSnapshots.IsEmpty)
        {
            return;
        }

        HashSet<string> currentOperatorXuids = await _bedrockPlayerDataService.GetOperatorXuidsAsync();
        foreach (KeyValuePair<string, HashSet<string>> pending in _pendingBedrockOpSnapshots.ToArray())
        {
            List<string> addedXuids = currentOperatorXuids
                .Where(xuid => !pending.Value.Contains(xuid))
                .ToList();

            if (addedXuids.Count != 1)
            {
                continue;
            }

            string addedXuid = addedXuids[0];
            string? knownName = await _bedrockPlayerDataService.GetNameAsync(addedXuid);
            if (knownName == null)
            {
                await _bedrockPlayerDataService.UpsertPlayerAsync(addedXuid, pending.Key);
            }

            _pendingBedrockOpSnapshots.TryRemove(pending.Key, out _);
        }
    }

    private async Task ToggleOpAsync(PlayerViewModel player)
    {
        if (!IsServerOnline)
        {
            return;
        }

        bool targetState = !player.IsOp;
        if (IsBedrock)
        {
            await ImportBedrockPlayerMapFromOutputBufferAsync();
            if (targetState && await _bedrockPlayerDataService.GetXuidAsync(player.Name) == null)
            {
                _pendingBedrockOpSnapshots[player.Name] = await _bedrockPlayerDataService.GetOperatorXuidsAsync();
            }
        }

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

        if (_pendingGamemodePlayers.TryRemove(player.Name, out PendingGamemodeChange? previousPending))
        {
            previousPending.Cancel();
            previousPending.Dispose();
        }

        if (UsesSidecarGamemode)
        {
            await _bedrockPlayerDataService.SaveGamemodeAsync(player.Name, mode);
        }

        var pending = new PendingGamemodeChange(mode, player.ConfirmedGameMode, shouldRevertPersistedGamemode: UsesSidecarGamemode);
        _pendingGamemodePlayers[player.Name] = pending;
        await DispatchCommandAsync($"gamemode {mode} {CommandFormatter.FormatPlayerName(player.Name, _metadata.ServerType)}");
        _ = RevertGamemodeAfterDelayAsync(player.Name, pending);
    }

    private async Task RevertGamemodeAfterDelayAsync(string playerName, PendingGamemodeChange pending)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), pending.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_pendingGamemodePlayers.TryGetValue(playerName, out PendingGamemodeChange? current) ||
            !ReferenceEquals(current, pending) ||
            !_pendingGamemodePlayers.TryRemove(playerName, out _))
        {
            return;
        }

        pending.Dispose();
        if (pending.ShouldRevertPersistedGamemode)
        {
            await _bedrockPlayerDataService.SaveGamemodeAsync(playerName, pending.PreviousMode);
        }

        _logger.LogWarning(
            "Gamemode change to {Mode} was not confirmed within 3 seconds for player {PlayerName} on instance {InstanceId}. Reverting UI selection.",
            pending.RequestedMode,
            playerName,
            _metadata.Id);

        await _dispatcher.InvokeAsync(() =>
        {
            PlayerViewModel? player = FindOnlinePlayer(playerName);
            player?.RevertGameModeFromPendingChange(pending.PreviousMode);
        });
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

        if (IsBedrock && commandName == "ban")
        {
            await ImportBedrockPlayerMapFromOutputBufferAsync();
            string? xuid = await _bedrockPlayerDataService.GetXuidAsync(player.Name);
            await _bedrockPlayerDataService.AddBanAsync(new BedrockBanEntry
            {
                Name = player.Name,
                Xuid = xuid ?? string.Empty,
                BannedAt = DateTime.UtcNow,
                Reason = sanitizedReason,
                BannedBy = "console"
            });
        }

        foreach (string command in PlayerActionCommandBuilder.BuildSubmitCommands(
                     action,
                     formattedName,
                     _metadata.ServerType,
                     sanitizedReason))
        {
            await DispatchCommandAsync(command);
        }

        if (IsBedrock && commandName == "ban")
        {
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

        foreach (string command in PlayerActionCommandBuilder.BuildPardonCommands(
                     CommandFormatter.FormatPlayerName(bannedPlayer.Name, _metadata.ServerType),
                     _metadata.ServerType))
        {
            await DispatchCommandAsync(command);
        }

        if (IsBedrock)
        {
            await _bedrockPlayerDataService.RemoveBanAsync(bannedPlayer.Name);
            await RefreshPersistentStateAsync();
        }
        else if (bannedPlayer.IsSidecar)
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
        if (IsBedrock)
        {
            CaptureBedrockPlayerMap(line);
            CaptureBedrockGamemode(line);
        }

        if (!line.Contains("game mode", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (KeyValuePair<string, PendingGamemodeChange> pendingEntry in _pendingGamemodePlayers.ToArray())
        {
            string playerName = pendingEntry.Key;
            PendingGamemodeChange pending = pendingEntry.Value;
            if (!line.Contains(playerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!LineConfirmsGamemode(line, pending.RequestedMode))
            {
                continue;
            }

            if (!_pendingGamemodePlayers.TryRemove(playerName, out PendingGamemodeChange? removed))
            {
                continue;
            }

            removed.Cancel();
            removed.Dispose();
            _ = _dispatcher.InvokeAsync(async () =>
            {
                PlayerViewModel? player = FindOnlinePlayer(playerName);
                if (player != null)
                {
                    player.ConfirmGameModeChange(pending.RequestedMode);
                    await player.FlashSuccessAsync();
                }
            });
        }
    }

    private void CaptureBedrockPlayerMap(string line)
    {
        Match match = BedrockConnectionRegex.Match(line);
        if (!match.Success)
        {
            return;
        }

        string name = match.Groups["name"].Value.Trim();
        string xuid = match.Groups["xuid"].Value.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                await _bedrockPlayerDataService.UpsertPlayerAsync(xuid, name);
                BedrockBanEntry? ban = await _bedrockPlayerDataService.GetBanForPlayerAsync(name, xuid);
                if (ban != null && IsServerOnline)
                {
                    await KickBedrockBannedPlayerAsync(name, ban);
                }

                await RefreshPersistentStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist Bedrock XUID mapping for {PlayerName} on instance {InstanceId}.", name, _metadata.Id);
            }
        });
    }

    private async Task KickBedrockBannedPlayerAsync(string playerName, BedrockBanEntry ban)
    {
        string formattedName = CommandFormatter.FormatPlayerName(playerName, _metadata.ServerType);
        string command = CommandFormatter.AppendOptionalReason($"kick {formattedName}", ban.Reason);
        await DispatchCommandAsync(command);
    }

    private void CaptureBedrockGamemode(string line)
    {
        Match match = BedrockGamemodeRegex.Match(line);
        if (!match.Success)
        {
            return;
        }

        string name = match.Groups["name"].Value.Trim();
        string mode = NormalizeGamemode(match.Groups["mode"].Value);
        _ = Task.Run(async () =>
        {
            try
            {
                await _bedrockPlayerDataService.SaveGamemodeAsync(name, mode);
                await _dispatcher.InvokeAsync(() =>
                {
                    PlayerViewModel? player = FindOnlinePlayer(name);
                    player?.SetGameModeFromServer(mode);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist Bedrock gamemode confirmation for {PlayerName} on instance {InstanceId}.", name, _metadata.Id);
            }
        });
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

    private PlayerViewModel? FindOnlinePlayer(string playerName)
    {
        return OnlinePlayers.FirstOrDefault(player =>
            string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LineConfirmsGamemode(string line, string mode)
    {
        return line.Contains(mode, StringComparison.OrdinalIgnoreCase) ||
               line.Contains($"{GetGamemodeDisplayName(mode)} Mode", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGamemodeDisplayName(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "creative" => "Creative",
            "adventure" => "Adventure",
            "spectator" => "Spectator",
            _ => "Survival"
        };
    }

    private static string NormalizeGamemode(string mode)
    {
        return mode.Trim().ToLowerInvariant() switch
        {
            "creative" => "creative",
            "adventure" => "adventure",
            "spectator" => "spectator",
            _ => "survival"
        };
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
        _playerDataWatcher.Dispose();
        _bedrockPermissionsWatcher.Dispose();
        foreach (PendingGamemodeChange pending in _pendingGamemodePlayers.Values)
        {
            pending.Cancel();
            pending.Dispose();
        }

        _pendingGamemodePlayers.Clear();
        _stateRefreshLock.Dispose();
        _serverProcess.OnOnlinePlayersUpdated -= OnOnlinePlayersUpdated;
        _serverProcess.OnOutputLine -= OnOutputLine;
        _serverProcess.OnStateChanged -= OnServerStateChanged;
    }

    private sealed class PendingGamemodeChange : IDisposable
    {
        private readonly CancellationTokenSource _timeout = new();

        public PendingGamemodeChange(string requestedMode, string previousMode, bool shouldRevertPersistedGamemode)
        {
            RequestedMode = requestedMode;
            PreviousMode = previousMode;
            ShouldRevertPersistedGamemode = shouldRevertPersistedGamemode;
        }

        public string RequestedMode { get; }
        public string PreviousMode { get; }
        public bool ShouldRevertPersistedGamemode { get; }
        public CancellationToken CancellationToken => _timeout.Token;

        public void Cancel()
        {
            _timeout.Cancel();
        }

        public void Dispose()
        {
            _timeout.Dispose();
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
