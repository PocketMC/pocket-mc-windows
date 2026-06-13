using System.IO;
using System.Windows.Media.Imaging;
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
    private readonly RemoteAuthenticationService _authenticationService;

    public RemoteControlSettingsViewModel(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteControlCoordinator coordinator,
        IDialogService dialogService,
        RemoteAuthenticationService authenticationService)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _coordinator = coordinator;
        _dialogService = dialogService;
        _authenticationService = authenticationService;

        var remote = _applicationState.Settings.RemoteControl;
        _isEnabled = remote.Enabled;
        _port = remote.Port;
        _allowRemoteConsoleCommands = remote.AllowRemoteConsoleCommands;
        _allowRemotePlayerActions = remote.AllowRemotePlayerActions;
        _requireAuthentication = remote.RequireAuthentication;
        _accessMode = remote.AccessMode == RemoteAccessMode.LanOnly 
            ? RemoteAccessMode.CloudflaredQuickTunnel 
            : remote.AccessMode;

        _isDiscordLinked = !string.IsNullOrEmpty(_applicationState.Settings.DiscordUserId);
        _enableDiscordNotifications = _applicationState.Settings.EnableDiscordNotifications;

        _settingsManager.SettingsSaved += OnSettingsSaved;

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
    private bool _requireAuthentication;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscordNotLinked))]
    private bool _isDiscordLinked;

    public bool IsDiscordNotLinked => !IsDiscordLinked;

    [ObservableProperty]
    private bool _enableDiscordNotifications;



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
    [NotifyPropertyChangedFor(nameof(IsPublicUrlCardVisible))]
    private string? _publicUrl;

    [ObservableProperty]
    private string _publicUrlProviderName = "";

    public bool HasLocalUrl => !string.IsNullOrEmpty(LocalUrl);
    public bool HasPublicUrl => !string.IsNullOrEmpty(PublicUrl);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublicUrlCardVisible))]
    private bool _isLoadingPublicUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublicUrlError))]
    [NotifyPropertyChangedFor(nameof(IsPublicUrlCardVisible))]
    private string? _publicUrlErrorText;

    public bool HasPublicUrlError => !string.IsNullOrEmpty(PublicUrlErrorText);

    public bool IsPublicUrlCardVisible => HasPublicUrl || IsLoadingPublicUrl || HasPublicUrlError;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private BitmapImage? _localQrImage;

    [ObservableProperty]
    private BitmapImage? _publicQrImage;

    [ObservableProperty]
    private bool _isLocalQrVisible;

    [ObservableProperty]
    private bool _isPublicQrVisible;

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
        SaveSettings();
    }

    partial void OnAllowRemotePlayerActionsChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnRequireAuthenticationChanged(bool value)
    {
        SaveAndRestart();
    }

    partial void OnPasswordChanged(string value)
    {
        // Don't auto-save while typing, handled by the save command or blur in UI,
        // but if bound to property changed, we can save it.
        // Usually better to save when they finish typing. We will save it.
        if (string.IsNullOrEmpty(value)) return;
        SaveAndRestart();
    }

    partial void OnEnableDiscordNotificationsChanged(bool value)
    {
        if (_isUpdatingFromSettings) return;
        SaveSettings();
    }

    private bool _isUpdatingFromSettings;

    private void OnSettingsSaved(object? sender, Models.AppSettings settings)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _isUpdatingFromSettings = true;
            try
            {
                IsDiscordLinked = !string.IsNullOrEmpty(settings.DiscordUserId);
                EnableDiscordNotifications = settings.EnableDiscordNotifications;
            }
            finally
            {
                _isUpdatingFromSettings = false;
            }
        });
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
        settings.RemoteControl.RequireAuthentication = RequireAuthentication;

        if (!string.IsNullOrEmpty(Password))
        {
            settings.RemoteControl.PasswordHash = _authenticationService.HashPassword(Password);
        }

        settings.EnableDiscordNotifications = EnableDiscordNotifications;

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
        SetStatus("", false);
        try
        {
            if (IsEnabled)
            {
                IsLoadingPublicUrl = true;
                PublicUrlErrorText = null;
                PublicUrl = null;
                PublicUrlProviderName = AccessMode switch
                {
                    RemoteAccessMode.CloudflaredQuickTunnel => "Cloudflare",
                    RemoteAccessMode.PlayitHttpsTunnel => "PlayIt",
                    _ => "Remote"
                };
                await _coordinator.RestartAllAsync();
            }
            else
            {
                await _coordinator.StopAllAsync();
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

        PublicUrlErrorText = status.TunnelError;

        IsLoadingPublicUrl = false;

        // Generate QR Codes
        LocalQrImage = GenerateQrCode(LocalUrl);
        PublicQrImage = GenerateQrCode(PublicUrl);

        // Hide QR panels if URLs are no longer active
        if (string.IsNullOrEmpty(LocalUrl))
        {
            IsLocalQrVisible = false;
        }
        if (string.IsNullOrEmpty(PublicUrl))
        {
            IsPublicQrVisible = false;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = isError ? message : "";
        IsStatusError = isError;
    }

    [RelayCommand]
    private async Task CopyLocalUrl()
    {
        await Infrastructure.ClipboardHelper.TrySetTextAsync(LocalUrl!);
    }

    [RelayCommand]
    private async Task CopyPublicUrl()
    {
        await Infrastructure.ClipboardHelper.TrySetTextAsync(PublicUrl!);
    }

    [RelayCommand]
    private void JoinDiscord()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://discord.gg/kTSUppTJ5C",
            UseShellExecute = true
        });
    }



    [RelayCommand]
    private void ToggleLocalQr()
    {
        IsLocalQrVisible = !IsLocalQrVisible;
    }

    [RelayCommand]
    private void TogglePublicQr()
    {
        IsPublicQrVisible = !IsPublicQrVisible;
    }

    private static BitmapImage? GenerateQrCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var qrGenerator = new QRCoder.QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.Q);
            using var pngQrCode = new QRCoder.PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = pngQrCode.GetGraphic(10);
            
            using var ms = new MemoryStream(qrCodeBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze(); // Crucial for multi-threaded/UI binding use
            return image;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to generate QR code: {ex}");
            return null;
        }
    }
}
