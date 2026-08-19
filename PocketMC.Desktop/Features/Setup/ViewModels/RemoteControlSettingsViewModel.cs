using PocketMC.Infrastructure.Configuration;
using PocketMC.RemoteControl.Models;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PocketMC.RemoteControl.Hosting;
using PocketMC.Domain.Models;
using PocketMC.RemoteControl.Services;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Services.Instances;
using System;
using PocketMC.Desktop.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Linq;

namespace PocketMC.Desktop.Features.Setup.ViewModels;

public sealed partial class RemoteControlSettingsViewModel : ObservableObject, IDisposable
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
    private readonly InstanceRegistry? _instanceRegistry;

    public RemoteControlSettingsViewModel(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteControlCoordinator coordinator,
        IDialogService dialogService,
        RemoteAuthenticationService authenticationService,
        InstanceRegistry? instanceRegistry = null)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _coordinator = coordinator;
        _dialogService = dialogService;
        _authenticationService = authenticationService;
        _instanceRegistry = instanceRegistry;

        var remote = _applicationState.Settings.RemoteControl;
        _isEnabled = remote.Enabled;
        _port = remote.Port <= 0 ? 25580 : remote.Port;

        _requireAuthentication = remote.RequireAuthentication;
        _username = remote.Username;
        _accessMode = remote.AccessMode == RemoteAccessMode.LanOnly
            ? RemoteAccessMode.CloudflaredQuickTunnel
            : remote.AccessMode;

        _isDiscordLinked = !string.IsNullOrEmpty(_applicationState.Settings.DiscordUserId);
        _enableDiscordNotifications = _applicationState.Settings.EnableDiscordNotifications;

        if (!string.IsNullOrEmpty(remote.ProtectedPassword))
        {
            try
            {
                _password = PocketMC.Infrastructure.Security.DataProtector.Unprotect(remote.ProtectedPassword) ?? string.Empty;
            }
            catch (Exception)
            {
                _password = string.Empty;
            }
        }

        _settingsManager.SettingsSaved += OnSettingsSaved;

        if (_instanceRegistry != null)
        {
            _instanceRegistry.InstancesChanged += OnInstancesChanged;
        }

        var allInstances = _instanceRegistry?.GetAll() ?? Array.Empty<InstanceMetadata>();
        foreach (var user in remote.Users ?? new List<RemoteControlUser>())
        {
            Users.Add(new RemoteControlUserViewModel(user, this, allInstances));
        }

        UpdateStatus();
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            var allInstances = _instanceRegistry?.GetAll() ?? Array.Empty<InstanceMetadata>();
            foreach (var userVm in Users)
            {
                userVm.PopulateAvailableInstances(allInstances);
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(Update);
        }
        else
        {
            Update();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab))]
    [NotifyPropertyChangedFor(nameof(IsUsersTab))]
    private int _selectedTab = 0;

    public bool IsGeneralTab
    {
        get => SelectedTab == 0;
        set { if (value) SelectedTab = 0; }
    }
    
    public bool IsUsersTab
    {
        get => SelectedTab == 1;
        set { if (value) SelectedTab = 1; }
    }

    public ObservableCollection<RemoteControlUserViewModel> Users { get; } = new();

    [ObservableProperty]
    private RemoteControlUserViewModel? _selectedUser;

    [RelayCommand]
    private void AddUser()
    {
        var user = new RemoteControlUser();
        var allInstances = _instanceRegistry?.GetAll() ?? Array.Empty<InstanceMetadata>();
        var userVm = new RemoteControlUserViewModel(user, this, allInstances) { IsEditing = true };
        Users.Add(userVm);
        SelectedUser = userVm;
    }

    public async Task RemoveUser(RemoteControlUserViewModel user)
    {
        var result = await _dialogService.ShowDialogAsync(
            "Remove User",
            $"Are you sure you want to remove the user '{user.Username}'? This action cannot be undone.",
            PocketMC.Desktop.Core.Interfaces.DialogType.Warning,
            false,
            "Remove",
            "Cancel");

        if (result == PocketMC.Desktop.Core.Interfaces.DialogResult.Ok || result == PocketMC.Desktop.Core.Interfaces.DialogResult.Yes)
        {
            Users.Remove(user);
            SaveSettings();
            SetStatus($"User '{user.Username}' removed successfully.", false);
        }
    }

    public void SaveUser(RemoteControlUserViewModel user)
    {
        if (string.IsNullOrWhiteSpace(user.Username))
        {
            SetStatus("Username cannot be empty.", true);
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                Users.Remove(user);
            }
            else
            {
                user.Username = user.SavedUsername;
                user.Model.Username = user.SavedUsername;
            }
            return;
        }

        var trimmedUsername = user.Username.Trim();

        // Check if username matches admin username
        var adminUser = _applicationState.Settings.RemoteControl.Username;
        if (!string.IsNullOrEmpty(adminUser) && string.Equals(trimmedUsername, adminUser.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Sub-user username cannot be the same as the admin username.", true);
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                Users.Remove(user);
            }
            else
            {
                user.Username = user.SavedUsername;
                user.Model.Username = user.SavedUsername;
            }
            return;
        }

        // Check if username is already taken by another sub-user
        if (Users.Any(u => u != user && string.Equals(u.Username?.Trim(), trimmedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"User '{trimmedUsername}' already exists.", true);
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                Users.Remove(user);
            }
            else
            {
                user.Username = user.SavedUsername;
                user.Model.Username = user.SavedUsername;
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Password))
        {
            SetStatus("Password is required to save credentials.", true);
            return;
        }

        var hashed = _authenticationService.HashPassword(user.Password);

        user.Username = trimmedUsername;
        user.SavedUsername = trimmedUsername;
        user.PasswordHash = hashed;
        user.ProtectedPassword = PocketMC.Infrastructure.Security.DataProtector.Protect(user.Password);

        user.IsEditing = false;
        if (!SaveSettings()) return;
        SetStatus($"User '{user.Username}' credentials saved successfully.", false);
    }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _port;



    [ObservableProperty]
    private bool _requireAuthentication;

    [ObservableProperty]
    private string? _username;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isAdminCredentialsExpanded;

    public bool IsPasswordSet => !string.IsNullOrEmpty(_applicationState.Settings.RemoteControl.PasswordHash);
    public bool IsPasswordNotSet => string.IsNullOrEmpty(_applicationState.Settings.RemoteControl.PasswordHash);
    public bool IsUsernameNotSet => string.IsNullOrEmpty(_applicationState.Settings.RemoteControl.Username);

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
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    private string _statusText = "";

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

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
        if (value && RequireAuthentication && (IsPasswordNotSet || IsUsernameNotSet) && !_isUpdatingFromSettings)
        {
            _ = HandleEnableWithCredentialsPromptAsync();
        }
        else
        {
            SaveAndRestart();
        }
    }

    private async Task HandleEnableWithCredentialsPromptAsync()
    {
        bool askUsername = IsUsernameNotSet;
        bool askPassword = IsPasswordNotSet;
        
        while (true)
        {
            var result = await _dialogService.PromptCredentialsAsync(
                "Setup Admin Credentials",
                "Remote Control requires authentication to be secure. Please set up the primary admin account, or turn off authentication to continue without it.",
                askUsername,
                askPassword);

            if (result.Username == null && result.Password == null)
            {
                RequireAuthentication = false;
                SaveAndRestart();
                break;
            }

            if ((askUsername && string.IsNullOrWhiteSpace(result.Username)) || 
                (askPassword && string.IsNullOrWhiteSpace(result.Password)))
            {
                _dialogService.ShowMessage("Invalid Input", "Username and password cannot be empty. Please enter valid credentials.", PocketMC.Desktop.Core.Interfaces.DialogType.Warning);
                continue;
            }

            if (askUsername) Username = result.Username;
            if (askPassword) Password = result.Password!;
            SaveCredentials();
            SaveAndRestart();
            break;
        }
    }

    partial void OnPortChanged(int value)
    {
        SaveAndRestart();
    }



    partial void OnRequireAuthenticationChanged(bool value)
    {
        if (value)
        {
            _applicationState.Settings.RemoteControl.SecurityStamp = Guid.NewGuid().ToString();
        }
        SaveSettings();
    }

    [RelayCommand]
    private void SaveCredentials()
    {
        if (string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Password))
        {
            Username = string.Empty;
            Password = string.Empty;
            _applicationState.Settings.RemoteControl.Username = null;
            _applicationState.Settings.RemoteControl.PasswordHash = null;
            _applicationState.Settings.RemoteControl.ProtectedPassword = null;
            
            SaveSettings();
            OnPropertyChanged(nameof(IsPasswordSet));
            OnPropertyChanged(nameof(IsPasswordNotSet));
            SetStatus("Admin credentials cleared successfully.", false);
            IsAdminCredentialsExpanded = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            SetStatus("Username cannot be empty.", true);
            Username = _applicationState.Settings.RemoteControl.Username ?? "";
            return;
        }
        
        if (string.IsNullOrWhiteSpace(Password))
        {
            SetStatus("Password cannot be empty.", true);
            return;
        }

        var trimmedAdminUsername = Username.Trim();

        // Check if any sub-user already uses this username
        if (Users.Any(u => string.Equals(u.Username?.Trim(), trimmedAdminUsername, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("Admin username cannot be the same as an existing sub-user username.", true);
            Username = _applicationState.Settings.RemoteControl.Username ?? "";
            return;
        }

        Username = trimmedAdminUsername;
        _applicationState.Settings.RemoteControl.SecurityStamp = Guid.NewGuid().ToString();
        SaveSettings();
        OnPropertyChanged(nameof(IsPasswordSet));
        OnPropertyChanged(nameof(IsPasswordNotSet));
        SetStatus("Admin credentials saved successfully.", false);
        IsAdminCredentialsExpanded = false;
    }

    partial void OnEnableDiscordNotificationsChanged(bool value)
    {
        if (_isUpdatingFromSettings) return;
        SaveSettings();
    }

    private bool _isUpdatingFromSettings;

    private void OnSettingsSaved(object? sender, PocketMC.Domain.Models.AppSettings settings)
    {
        void Update()
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
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(Update);
        }
        else
        {
            Update();
        }
    }



    partial void OnAccessModeChanged(RemoteAccessMode value)
    {
        SaveAndRestart();
    }





    internal bool SaveSettings()
    {
        var settings = _applicationState.Settings;
        if (Port <= 0 || Port > 65535)
        {
            SetStatus("Remote Control port must be between 1 and 65535.", true);
            return false;
        }

        settings.RemoteControl.Enabled = IsEnabled;
        settings.RemoteControl.Port = Port;

        settings.RemoteControl.AccessMode = AccessMode;
        settings.RemoteControl.TunnelProviderId = MapRemoteAccessModeToProviderId(AccessMode);
        settings.RemoteControl.RequireAuthentication = RequireAuthentication;
        settings.RemoteControl.Username = Username;

        if (!string.IsNullOrEmpty(Password))
        {
            settings.RemoteControl.PasswordHash = _authenticationService.HashPassword(Password);
            settings.RemoteControl.ProtectedPassword = PocketMC.Infrastructure.Security.DataProtector.Protect(Password);
        }
        else
        {
            settings.RemoteControl.PasswordHash = null;
            settings.RemoteControl.ProtectedPassword = null;
        }

        settings.EnableDiscordNotifications = EnableDiscordNotifications;

        settings.RemoteControl.Users = Users.Select(u => u.Model).ToList();

        _settingsManager.Save(settings);

        OnPropertyChanged(nameof(IsPasswordSet));
        OnPropertyChanged(nameof(IsPasswordNotSet));
        OnPropertyChanged(nameof(IsOwnerSetupVisible));

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

    private CancellationTokenSource? _statusCts;

    private void SetStatus(string message, bool isError, int autoDismissSeconds = 5)
    {
        _statusCts?.Cancel();
        _statusCts?.Dispose();
        _statusCts = null;

        StatusText = message;
        IsStatusError = isError;

        if (!string.IsNullOrEmpty(message) && autoDismissSeconds > 0)
        {
            var cts = new CancellationTokenSource();
            _statusCts = cts;
            _ = ClearStatusAfterDelayAsync(cts.Token, autoDismissSeconds);
        }
    }

    private async Task ClearStatusAfterDelayAsync(CancellationToken token, int seconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            if (!token.IsCancellationRequested)
            {
                StatusText = "";
                IsStatusError = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation when a new status is posted
        }
    }

    public void Dispose()
    {
        _settingsManager.SettingsSaved -= OnSettingsSaved;

        if (_instanceRegistry != null)
        {
            _instanceRegistry.InstancesChanged -= OnInstancesChanged;
        }

        _statusCts?.Cancel();
        _statusCts?.Dispose();
        _statusCts = null;
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
            FileName = PocketMC.Infrastructure.Configuration.AppConfig.LinkDiscord,
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

    public bool IsOwnerSetupVisible => RequireAuthentication && IsPasswordNotSet;
}

public partial class RemoteControlUserViewModel : ObservableObject
{
    private readonly RemoteControlSettingsViewModel _parent;
    public RemoteControlUser Model { get; }

    public ObservableCollection<UserInstanceAccessItemViewModel> AvailableInstances { get; } = new();

    public string SavedUsername { get; set; } = "";

    public RemoteControlUserViewModel(
        RemoteControlUser model,
        RemoteControlSettingsViewModel parent,
        IReadOnlyList<InstanceMetadata>? instances = null)
    {
        Model = model;
        _parent = parent;
        _username = model.Username ?? "";
        SavedUsername = model.Username ?? "";
        _password = "";
        _allowRemoteConsoleCommands = model.AllowRemoteConsoleCommands;
        _allowRemotePlayerActions = model.AllowRemotePlayerActions;
        _allowRemoteServerSettings = model.AllowRemoteServerSettings;
        _allowRemoteServerAddons = model.AllowRemoteServerAddons;
        _allowRemoteFileManager = model.AllowRemoteFileManager;
        _allowRemoteServerBackups = model.AllowRemoteServerBackups;
        _allowAllInstances = model.AllowAllInstances;
        _passwordHash = model.PasswordHash;
        _protectedPassword = model.ProtectedPassword;
        
        if (!string.IsNullOrEmpty(_protectedPassword))
        {
            try
            {
                _password = PocketMC.Infrastructure.Security.DataProtector.Unprotect(_protectedPassword) ?? "";
            }
            catch
            {
                _password = "";
            }
        }

        PopulateAvailableInstances(instances);
    }

    public void PopulateAvailableInstances(IReadOnlyList<InstanceMetadata>? instances)
    {
        AvailableInstances.Clear();
        if (instances == null || instances.Count == 0)
        {
            OnPropertyChanged(nameof(HasAvailableInstances));
            OnPropertyChanged(nameof(HasNoAvailableInstances));
            return;
        }

        var selectedIds = new HashSet<Guid>(Model.AllowedInstanceIds ?? Enumerable.Empty<Guid>());

        foreach (var inst in instances.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool isSelected = selectedIds.Contains(inst.Id);
            var itemVm = new UserInstanceAccessItemViewModel(
                inst.Id,
                inst.Name,
                inst.ServerType,
                inst.MinecraftVersion ?? "",
                isSelected,
                OnInstanceSelectionChanged);

            AvailableInstances.Add(itemVm);
        }

        OnPropertyChanged(nameof(HasAvailableInstances));
        OnPropertyChanged(nameof(HasNoAvailableInstances));
    }

    public bool HasAvailableInstances => AvailableInstances.Count > 0;
    public bool HasNoAvailableInstances => AvailableInstances.Count == 0;

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _password;

    public string PasswordHash 
    { 
        get => _passwordHash; 
        set 
        { 
            _passwordHash = value; 
            Model.PasswordHash = value; 
        } 
    }
    private string _passwordHash = "";

    public string? ProtectedPassword 
    { 
        get => _protectedPassword; 
        set 
        { 
            _protectedPassword = value; 
            Model.ProtectedPassword = value; 
        } 
    }
    private string? _protectedPassword;

    [ObservableProperty]
    private bool _allowRemoteConsoleCommands;

    [ObservableProperty]
    private bool _allowRemotePlayerActions;

    [ObservableProperty]
    private bool _allowRemoteServerSettings;

    [ObservableProperty]
    private bool _allowRemoteServerAddons;

    [ObservableProperty]
    private bool _allowRemoteFileManager;

    [ObservableProperty]
    private bool _allowRemoteServerBackups;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestrictedInstances))]
    private bool _allowAllInstances;

    public bool IsRestrictedInstances => !AllowAllInstances;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    private void OnInstanceSelectionChanged()
    {
        Model.AllowedInstanceIds = AvailableInstances
            .Where(i => i.IsSelected)
            .Select(i => i.InstanceId)
            .ToList();

        _parent.SaveSettings();
    }

    partial void OnUsernameChanged(string value) => Model.Username = value;
    partial void OnAllowRemoteConsoleCommandsChanged(bool value) { Model.AllowRemoteConsoleCommands = value; _parent.SaveSettings(); }
    partial void OnAllowRemotePlayerActionsChanged(bool value) { Model.AllowRemotePlayerActions = value; _parent.SaveSettings(); }
    partial void OnAllowRemoteServerSettingsChanged(bool value) { Model.AllowRemoteServerSettings = value; _parent.SaveSettings(); }
    partial void OnAllowRemoteServerAddonsChanged(bool value) { Model.AllowRemoteServerAddons = value; _parent.SaveSettings(); }
    partial void OnAllowRemoteFileManagerChanged(bool value) { Model.AllowRemoteFileManager = value; _parent.SaveSettings(); }
    partial void OnAllowRemoteServerBackupsChanged(bool value) { Model.AllowRemoteServerBackups = value; _parent.SaveSettings(); }
    partial void OnAllowAllInstancesChanged(bool value) { Model.AllowAllInstances = value; _parent.SaveSettings(); }

    partial void OnIsEditingChanged(bool value)
    {
        if (!value && string.IsNullOrEmpty(PasswordHash))
        {
            _parent.Users.Remove(this);
        }
    }

    [RelayCommand]
    private void Edit()
    {
        SavedUsername = Username ?? "";
        if (!string.IsNullOrEmpty(ProtectedPassword))
        {
            try
            {
                Password = PocketMC.Infrastructure.Security.DataProtector.Unprotect(ProtectedPassword) ?? "";
            }
            catch
            {
                Password = "";
            }
        }
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (string.IsNullOrEmpty(PasswordHash))
        {
            _parent.Users.Remove(this);
        }
        else
        {
            Username = SavedUsername;
            Model.Username = SavedUsername;
            IsEditing = false;
        }
    }

    [RelayCommand]
    private void Save() => _parent.SaveUser(this);

    [RelayCommand]
    private async Task Remove() => await _parent.RemoveUser(this);
}

public sealed partial class UserInstanceAccessItemViewModel : ObservableObject
{
    public Guid InstanceId { get; }
    public string InstanceName { get; }
    public string ServerType { get; }
    public string MinecraftVersion { get; }

    [ObservableProperty]
    private bool _isSelected;

    private readonly Action? _onChanged;

    public UserInstanceAccessItemViewModel(
        Guid instanceId,
        string instanceName,
        string serverType,
        string minecraftVersion,
        bool isSelected,
        Action? onChanged = null)
    {
        InstanceId = instanceId;
        InstanceName = instanceName;
        ServerType = serverType;
        MinecraftVersion = minecraftVersion;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _onChanged?.Invoke();
    }
}



