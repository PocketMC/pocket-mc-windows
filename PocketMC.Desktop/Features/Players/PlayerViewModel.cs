using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Players;

public sealed class PlayerViewModel : ViewModelBase
{
    private readonly Func<PlayerViewModel, Task> _toggleOpAsync;
    private readonly Func<PlayerViewModel, string, Task> _changeGamemodeAsync;
    private readonly Func<PlayerViewModel, string, string, Task<bool>> _submitReasonAsync;
    private bool _isOp;
    private bool _isBanned;
    private bool _isOpLoading;
    private bool _isOpUpdating;
    private bool _isGamemodeLoading;
    private bool _isServerOnline;
    private bool _isRecentlyUpdated;
    private bool _isReasonPromptVisible;
    private string _selectedGameMode = "survival";
    private string _confirmedGameMode = "survival";
    private string _pendingReason = string.Empty;
    private string _pendingReasonAction = string.Empty;

    public PlayerViewModel(
        string name,
        Func<PlayerViewModel, Task> toggleOpAsync,
        Func<PlayerViewModel, string, Task> changeGamemodeAsync,
        Func<PlayerViewModel, string, string, Task<bool>> submitReasonAsync)
    {
        Name = name;
        _toggleOpAsync = toggleOpAsync;
        _changeGamemodeAsync = changeGamemodeAsync;
        _submitReasonAsync = submitReasonAsync;

        ToggleOpCommand = new AsyncRelayCommand(_ => ToggleOpAsync(), _ => CanToggleOp);
        KickCommand = new RelayCommand(_ => ShowReasonPrompt("Kick"), _ => CanSendCommands);
        BanCommand = new RelayCommand(_ => ShowReasonPrompt("Ban"), _ => CanSendCommands && !IsBanned);
        ConfirmReasonCommand = new AsyncRelayCommand(_ => ConfirmReasonAsync(), _ => CanSendCommands && IsReasonPromptVisible);
        CancelReasonCommand = new RelayCommand(_ => HideReasonPrompt());
    }

    public string Name { get; }
    public ObservableCollection<string> GameModes { get; } = new(new[] { "survival", "creative", "adventure", "spectator" });
    public ICommand ToggleOpCommand { get; }
    public ICommand KickCommand { get; }
    public ICommand BanCommand { get; }
    public ICommand ConfirmReasonCommand { get; }
    public ICommand CancelReasonCommand { get; }

    public bool IsOp
    {
        get => _isOp;
        private set => SetProperty(ref _isOp, value);
    }

    public bool IsOpLoading
    {
        get => _isOpLoading;
        set
        {
            if (SetProperty(ref _isOpLoading, value))
            {
                OnPropertyChanged(nameof(IsOpBusy));
                OnPropertyChanged(nameof(CanToggleOp));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsBanned
    {
        get => _isBanned;
        set
        {
            if (SetProperty(ref _isBanned, value))
            {
                OnPropertyChanged(nameof(BanButtonText));
                OnPropertyChanged(nameof(BanTooltip));
            }
        }
    }

    public bool IsOpUpdating
    {
        get => _isOpUpdating;
        set
        {
            if (SetProperty(ref _isOpUpdating, value))
            {
                OnPropertyChanged(nameof(IsOpBusy));
                OnPropertyChanged(nameof(CanToggleOp));
                OnPropertyChanged(nameof(CanSendCommands));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsOpBusy => IsOpLoading || IsOpUpdating;

    public bool IsServerOnline
    {
        get => _isServerOnline;
        set
        {
            if (SetProperty(ref _isServerOnline, value))
            {
                OnPropertyChanged(nameof(CanSendCommands));
                OnPropertyChanged(nameof(CanToggleOp));
                OnPropertyChanged(nameof(CanChangeGamemode));
                OnPropertyChanged(nameof(OfflineTooltip));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanSendCommands => IsServerOnline;
    public bool CanToggleOp => CanSendCommands && !IsOpBusy;
    public bool CanChangeGamemode => CanSendCommands && !IsGamemodeLoading;

    public bool IsRecentlyUpdated
    {
        get => _isRecentlyUpdated;
        set => SetProperty(ref _isRecentlyUpdated, value);
    }

    public string SelectedGameMode
    {
        get => _selectedGameMode;
        set
        {
            string? normalizedMode = NormalizeGameMode(value);
            if (normalizedMode == null || !SetProperty(ref _selectedGameMode, normalizedMode))
            {
                return;
            }

            if (CanChangeGamemode && !string.Equals(normalizedMode, _confirmedGameMode, StringComparison.OrdinalIgnoreCase))
            {
                _ = _changeGamemodeAsync(this, normalizedMode);
            }
        }
    }

    public string ConfirmedGameMode
    {
        get => _confirmedGameMode;
        private set => SetProperty(ref _confirmedGameMode, value);
    }

    public bool IsGamemodeLoading
    {
        get => _isGamemodeLoading;
        set
        {
            if (SetProperty(ref _isGamemodeLoading, value))
            {
                OnPropertyChanged(nameof(CanChangeGamemode));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsReasonPromptVisible
    {
        get => _isReasonPromptVisible;
        private set => SetProperty(ref _isReasonPromptVisible, value);
    }

    public string PendingReason
    {
        get => _pendingReason;
        set => SetProperty(ref _pendingReason, value);
    }

    public string PendingReasonAction
    {
        get => _pendingReasonAction;
        private set
        {
            if (SetProperty(ref _pendingReasonAction, value))
            {
                OnPropertyChanged(nameof(ReasonPromptText));
            }
        }
    }

    public string ReasonPromptText => string.IsNullOrWhiteSpace(PendingReasonAction)
        ? "Reason"
        : $"{PendingReasonAction} reason";

    public string BanButtonText => IsBanned ? "Banned" : "Ban";
    public string BanTooltip => IsBanned ? "This player is already present in the persistent ban list." : "Ban this player";
    public string OfflineTooltip => CanSendCommands ? string.Empty : "Server must be online to send player commands.";
    public string GamemodeTooltip => "PocketMine spectator support may depend on installed plugins.";

    public void SetOpFromState(bool isOp)
    {
        IsOp = isOp;
        IsOpLoading = false;
        IsOpUpdating = false;
    }

    public void RefreshOpBinding()
    {
        OnPropertyChanged(nameof(IsOp));
    }

    public void SetGameModeSilently(string mode)
    {
        string? normalizedMode = NormalizeGameMode(mode);
        if (normalizedMode == null)
        {
            return;
        }

        _selectedGameMode = normalizedMode;
        OnPropertyChanged(nameof(SelectedGameMode));
    }

    public void SetGameModeFromServer(string mode)
    {
        string? normalizedMode = NormalizeGameMode(mode);
        if (normalizedMode == null)
        {
            return;
        }

        ConfirmedGameMode = normalizedMode;
        SetGameModeSilently(normalizedMode);
        IsGamemodeLoading = false;
    }

    public void ConfirmGameModeChange(string mode)
    {
        SetGameModeFromServer(mode);
    }

    public void RevertGameModeFromPendingChange(string previousMode)
    {
        SetGameModeFromServer(previousMode);
    }

    public async Task FlashSuccessAsync()
    {
        IsRecentlyUpdated = true;
        await Task.Delay(1600);
        IsRecentlyUpdated = false;
    }

    private async Task ToggleOpAsync()
    {
        RefreshOpBinding();
        await _toggleOpAsync(this);
    }

    private void ShowReasonPrompt(string action)
    {
        PendingReasonAction = action;
        PendingReason = string.Empty;
        IsReasonPromptVisible = true;
    }

    private async Task ConfirmReasonAsync()
    {
        string action = PendingReasonAction;
        string reason = PendingReason;
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        bool submitted = await _submitReasonAsync(this, action, reason);
        if (submitted)
        {
            HideReasonPrompt();
        }
    }

    private void HideReasonPrompt()
    {
        IsReasonPromptVisible = false;
        PendingReasonAction = string.Empty;
        PendingReason = string.Empty;
    }

    private static string? NormalizeGameMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        string normalizedMode = mode.Trim().ToLowerInvariant();
        return normalizedMode is "survival" or "creative" or "adventure" or "spectator"
            ? normalizedMode
            : null;
    }
}

public sealed class BannedPlayerViewModel : ViewModelBase
{
    private readonly Func<BannedPlayerViewModel, Task> _pardonAsync;
    private bool _isServerOnline;

    public BannedPlayerViewModel(
        string name,
        string reason,
        string banned,
        string expires,
        bool isSidecar,
        bool isServerOnline,
        Func<BannedPlayerViewModel, Task> pardonAsync)
    {
        Name = name;
        Reason = reason;
        Banned = banned;
        Expires = expires;
        IsSidecar = isSidecar;
        _isServerOnline = isServerOnline;
        _pardonAsync = pardonAsync;
        PardonCommand = new AsyncRelayCommand(_ => _pardonAsync(this), _ => IsServerOnline);
    }

    public string Name { get; }
    public string Reason { get; }
    public string Banned { get; }
    public string Expires { get; }
    public bool IsSidecar { get; }
    public ICommand PardonCommand { get; }
    public string ReasonDisplay => string.IsNullOrWhiteSpace(Reason) ? "No reason recorded" : Reason;
    public string BannedDisplay => string.IsNullOrWhiteSpace(Banned) ? "Unknown" : Banned;
    public string ExpiresDisplay => string.IsNullOrWhiteSpace(Expires) ? "forever" : Expires;

    public bool IsServerOnline
    {
        get => _isServerOnline;
        set
        {
            if (SetProperty(ref _isServerOnline, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
