using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Features.Setup.ViewModels;

public sealed partial class RemoteControlSettingsViewModel : ObservableObject
{
    public sealed record RemoteAccessModeOption(RemoteAccessMode Mode, string Label);

    public static IReadOnlyList<RemoteAccessModeOption> RemoteAccessModeOptions { get; } =
    [
        new(RemoteAccessMode.CloudflaredQuickTunnel, "Cloudflare Quick Tunnel"),
        new(RemoteAccessMode.PlayitHttpsTunnel, "PlayIt Premium HTTPS")
    ];



    public const string PlayitHttpsWarningText =
        "PlayIt HTTPS tunnels require PlayIt Premium. Stop Remote Link disables the dedicated PocketMC Remote Control tunnel.";

    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteControlCoordinator _coordinator;
    private readonly IDialogService _dialogService;

    public RemoteControlSettingsViewModel(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteControlCoordinator coordinator,
        IDialogService dialogService)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _coordinator = coordinator;
        _dialogService = dialogService;

        var remote = _applicationState.Settings.RemoteControl;
        _isEnabled = remote.Enabled;
        _port = remote.Port;
        _allowRemoteConsoleCommands = remote.AllowRemoteConsoleCommands;
        _allowRemotePlayerActions = remote.AllowRemotePlayerActions;
        _accessMode = remote.AccessMode == RemoteAccessMode.LanOnly 
            ? RemoteAccessMode.CloudflaredQuickTunnel 
            : remote.AccessMode;

        UpdateStatus();
    }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private bool _allowRemoteConsoleCommands;

    [ObservableProperty]
    private bool _allowRemotePlayerActions;



    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudflaredMode))]
    [NotifyPropertyChangedFor(nameof(IsPlayitHttpsMode))]
    private RemoteAccessMode _accessMode;

    public bool IsCloudflaredMode => AccessMode == RemoteAccessMode.CloudflaredQuickTunnel;
    public bool IsPlayitHttpsMode => AccessMode == RemoteAccessMode.PlayitHttpsTunnel;

    public IReadOnlyList<RemoteAccessModeOption> AccessModes => RemoteAccessModeOptions;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalUrl))]
    private string? _localUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublicUrl))]
    private string? _publicUrl;

    [ObservableProperty]
    private string _publicUrlProviderName = "";

    public bool HasLocalUrl => !string.IsNullOrEmpty(LocalUrl);
    public bool HasPublicUrl => !string.IsNullOrEmpty(PublicUrl);

    [ObservableProperty]
    private bool _isLoadingPublicUrl;

    [ObservableProperty]
    private bool _isStatusError;

    partial void OnIsEnabledChanged(bool value)
    {
        SaveAndRestart();
    }

    partial void OnPortChanged(int value)
    {
        SaveAndRestart();
    }

    partial void OnAllowRemoteConsoleCommandsChanged(bool value)
    {
        SaveAndRestart();
    }

    partial void OnAllowRemotePlayerActionsChanged(bool value)
    {
        SaveAndRestart();
    }



    partial void OnAccessModeChanged(RemoteAccessMode value)
    {
        SaveAndRestart();
    }





    private bool SaveSettings()
    {
        var settings = _applicationState.Settings;
        if (Port <= 0 || Port > 65535)
        {
            SetStatus("Remote Control port must be between 1 and 65535.", true);
            return false;
        }

        settings.RemoteControl.Enabled = IsEnabled;
        settings.RemoteControl.Port = Port;
        settings.RemoteControl.AllowRemoteConsoleCommands = AllowRemoteConsoleCommands;
        settings.RemoteControl.AllowRemotePlayerActions = AllowRemotePlayerActions;
        settings.RemoteControl.AccessMode = AccessMode;
        settings.RemoteControl.TunnelProviderId = MapRemoteAccessModeToProviderId(AccessMode);

        _settingsManager.Save(settings);
        return true;
    }

    public static string MapRemoteAccessModeToProviderId(RemoteAccessMode accessMode) =>
        accessMode switch
        {
            RemoteAccessMode.CloudflaredQuickTunnel => "cloudflared-quick",
            RemoteAccessMode.PlayitHttpsTunnel => "playit-https",
            _ => "none"
        };

    private bool _isRestarting;

    private async void SaveAndRestart()
    {
        if (_isRestarting) return;
        if (!SaveSettings()) return;

        _isRestarting = true;
        try
        {
            if (IsEnabled)
            {
                if (IsCloudflaredMode) IsLoadingPublicUrl = true;
                await _coordinator.RestartAllAsync();
                SetStatus("Settings applied.", false);
            }
            else
            {
                await _coordinator.StopAllAsync();
                SetStatus("Remote Control stopped.", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            _isRestarting = false;
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        RemoteDashboardStatus status = _coordinator.GetStatus();
        LocalUrl = status.LocalUrls.FirstOrDefault();
        
        PublicUrlProviderName = AccessMode switch
        {
            RemoteAccessMode.CloudflaredQuickTunnel => "Cloudflare",
            RemoteAccessMode.PlayitHttpsTunnel => "PlayIt",
            _ => "Remote"
        };
        PublicUrl = status.PublicUrl;

        if (!string.IsNullOrWhiteSpace(status.TunnelError))
        {
            SetStatus(status.TunnelError, true);
        }
        else
        {
            SetStatus("", false);
        }

        IsLoadingPublicUrl = false;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = isError ? message : "";
        IsStatusError = isError;
    }

    [RelayCommand]
    private void CopyLocalUrl()
    {
        if (!string.IsNullOrEmpty(LocalUrl))
            System.Windows.Clipboard.SetText(LocalUrl);
    }

    [RelayCommand]
    private void CopyPublicUrl()
    {
        if (!string.IsNullOrEmpty(PublicUrl))
            System.Windows.Clipboard.SetText(PublicUrl);
    }
}
