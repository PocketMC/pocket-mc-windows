using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.RemoteControl.Auth;

namespace PocketMC.Desktop.Features.Setup.ViewModels;

public sealed partial class RemoteControlSettingsViewModel : ObservableObject
{
    public sealed record RemoteAccessModeOption(RemoteAccessMode Mode, string Label);

    public static IReadOnlyList<RemoteAccessModeOption> RemoteAccessModeOptions { get; } =
    [
        new(RemoteAccessMode.LanOnly, "LAN only"),
        new(RemoteAccessMode.CloudflaredQuickTunnel, "Cloudflare Quick Tunnel"),
        new(RemoteAccessMode.PlayitHttpsTunnel, "PlayIt Premium HTTPS")
    ];

    public const string PlayitHttpsWarningText =
        "PlayIt HTTPS tunnels require PlayIt Premium. Stop Remote Link disables the dedicated PocketMC Remote Control tunnel.";

    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteControlCoordinator _coordinator;
    private readonly IDialogService _dialogService;
    private readonly RemoteAuthService _authService;

    public RemoteControlSettingsViewModel(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteControlCoordinator coordinator,
        IDialogService dialogService,
        RemoteAuthService authService)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _coordinator = coordinator;
        _dialogService = dialogService;
        _authService = authService;

        _authService.DevicesChanged += (s, e) => 
        {
            Application.Current.Dispatcher.Invoke(LoadDevices);
        };

        var remote = _applicationState.Settings.RemoteControl;
        _isEnabled = remote.Enabled;
        _port = remote.Port;
        _allowRemoteConsoleCommands = remote.AllowRemoteConsoleCommands;
        _allowRemotePlayerActions = remote.AllowRemotePlayerActions;
        _accessMode = remote.AccessMode;
        _cloudflaredPath = remote.CloudflaredPath ?? "";

        LoadDevices();
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

    [ObservableProperty]
    private string _cloudflaredPath;

    public ObservableCollection<RemoteDeviceSession> PairedDevices { get; } = new();

    public IReadOnlyList<RemoteAccessModeOption> AccessModes => RemoteAccessModeOptions;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _localUrlText = "";

    [ObservableProperty]
    private string _publicUrlText = "";

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _canStartTunnel;

    [ObservableProperty]
    private bool _canStopTunnel;

    [ObservableProperty]
    private bool _canPair;

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

    partial void OnCloudflaredPathChanged(string value)
    {
        SaveAndRestart();
    }

    [RelayCommand]
    private void BrowseCloudflaredPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select cloudflared.exe",
            Filter = "Executable Files|*.exe|All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            CloudflaredPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task StartTunnelAsync()
    {
        CanStartTunnel = false;
        try
        {
            if (!SaveSettings()) return;
            if (AccessMode == RemoteAccessMode.LanOnly)
            {
                AccessMode = RemoteAccessMode.CloudflaredQuickTunnel;
            }

            await _coordinator.RestartHostAsync();
            var result = await _coordinator.StartTunnelAsync();
            if (result.Success)
            {
                Clipboard.SetText(result.PublicUrl ?? "");
                SetStatus($"Tunnel started and copied.", false);
            }
            else
            {
                SetStatus(result.ErrorMessage ?? $"Could not start tunnel.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task StopTunnelAsync()
    {
        try
        {
            await _coordinator.StopTunnelAsync();
            SetStatus("Remote link stopped.", false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            UpdateStatus();
        }
    }

    [RelayCommand]
    private void CopyTunnelUrl()
    {
        RemoteDashboardStatus status = _coordinator.GetStatus();
        string? url = status.PublicUrl ?? status.LocalUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("No remote link is available yet.", true);
            return;
        }

        Clipboard.SetText(url);
        SetStatus("Remote link copied.", false);
    }

    [RelayCommand]
    private void PairDevice()
    {
        if (!IsEnabled)
        {
            SetStatus("Enable Remote Control before pairing a device.", true);
            return;
        }

        RemotePairingLink pairingLink = _coordinator.CreatePairingLink();
        Clipboard.SetText(pairingLink.Url);
        _dialogService.ShowMessage(
            "Pair Device",
            $"Open this link on your phone within 2 minutes:\n\n{pairingLink.Url}\n\nThe link was copied to your clipboard.");
        UpdateStatus();
    }

    [RelayCommand]
    private async Task RevokeAllDevicesAsync()
    {
        var result = await _dialogService.ShowDialogAsync(
            "Revoke Remote Devices",
            "Revoke all paired remote devices? They will need to pair again before controlling servers.",
            DialogType.Warning,
            showCancel: true,
            primaryButtonText: "Revoke");

        if (result == DialogResult.Ok || result == DialogResult.Yes)
        {
            _coordinator.RevokeAllDevices();
            SetStatus("All remote devices were revoked.", false);
            LoadDevices();
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task RevokeDeviceAsync(RemoteDeviceSession device)
    {
        var result = await _dialogService.ShowDialogAsync(
            "Revoke Device",
            $"Revoke access for {device.DisplayName}?",
            DialogType.Warning,
            showCancel: true,
            primaryButtonText: "Revoke");

        if (result == DialogResult.Ok || result == DialogResult.Yes)
        {
            _coordinator.RevokeDevice(device.Id);
            SetStatus($"Revoked {device.DisplayName}.", false);
            LoadDevices();
            UpdateStatus();
        }
    }

    private void LoadDevices()
    {
        PairedDevices.Clear();
        foreach (var device in _applicationState.Settings.RemoteControl.PairedDevices
                     .Where(d => !d.RevokedAtUtc.HasValue)
                     .OrderByDescending(d => d.CreatedAtUtc))
        {
            PairedDevices.Add(device);
        }
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
        settings.RemoteControl.CloudflaredPath = string.IsNullOrWhiteSpace(CloudflaredPath) ? null : CloudflaredPath.Trim();

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
                await _coordinator.RestartHostAsync();
                SetStatus("Settings applied.", false);
            }
            else
            {
                await _coordinator.StopHostAsync();
                await _coordinator.StopTunnelAsync();
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
        LocalUrlText = $"Local URL: {status.LocalUrls.FirstOrDefault() ?? "not available"}";
        
        string providerName = AccessMode switch
        {
            RemoteAccessMode.CloudflaredQuickTunnel => "Cloudflare",
            RemoteAccessMode.PlayitHttpsTunnel => "PlayIt",
            _ => "Remote"
        };
        PublicUrlText = $"{providerName} Public URL: {status.PublicUrl ?? "not started"}";
        
        CanStopTunnel = status.TunnelRunning;
        CanPair = status.Enabled;
        CanStartTunnel = status.Enabled;

        if (!string.IsNullOrWhiteSpace(status.TunnelError))
        {
            SetStatus(status.TunnelError, true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = message;
        IsStatusError = isError;
    }
}
