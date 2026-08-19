using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Backups;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Telemetry;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PocketMC.Desktop.Features.Settings;

public class CloudProviderViewModel : ViewModelBase
{
    private readonly ICloudBackupProvider _provider;
    private readonly IDialogService _dialogService;

    public CloudBackupProviderType ProviderType => _provider.ProviderType;

    private CloudBackupConnectionStatus _status;
    public CloudBackupConnectionStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string _accountInfo = "Checking...";
    public string AccountInfo
    {
        get => _accountInfo;
        set => SetProperty(ref _accountInfo, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public CloudProviderViewModel(ICloudBackupProvider provider, IDialogService dialogService)
    {
        _provider = provider;
        _dialogService = dialogService;
        ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsBusy && Status != CloudBackupConnectionStatus.Connected);
        DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => !IsBusy && Status == CloudBackupConnectionStatus.Connected);
    }

    public async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            var account = await _provider.GetAccountAsync(CancellationToken.None);
            if (account != null && account.Status == CloudBackupConnectionStatus.Connected)
            {
                Status = CloudBackupConnectionStatus.Connected;
                AccountInfo = $"Connected as {account.Email ?? account.DisplayName}";
            }
            else
            {
                Status = account?.Status ?? await _provider.GetStatusAsync(CancellationToken.None);
                AccountInfo = Status == CloudBackupConnectionStatus.Expired ? "Session expired. Please reconnect." : "Not connected.";
            }
        }
        catch (Exception)
        {
            Status = CloudBackupConnectionStatus.Error;
            AccountInfo = "Could not check account status.";
        }
        finally
        {
            IsBusy = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ConnectAsync()
    {
        IsBusy = true;
        try
        {
            await _provider.ConnectAsync(CancellationToken.None);
            await RefreshStatusAsync();
            _dialogService.ShowMessage("Success", $"Successfully connected to {ProviderType}.");
        }
        catch (Exception ex)
        {
            string userFriendlyMessage = GetUserFriendlyErrorMessage(ex, $"Failed to connect to {ProviderType}. Please check your connection.");
            _dialogService.ShowMessage("Connection Failed", userFriendlyMessage, DialogType.Error);
            await RefreshStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            await _provider.DisconnectAsync(CancellationToken.None);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            string userFriendlyMessage = GetUserFriendlyErrorMessage(ex, $"Failed to disconnect from {ProviderType}.");
            _dialogService.ShowMessage("Disconnection Error", userFriendlyMessage, DialogType.Error);
            await RefreshStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GetUserFriendlyErrorMessage(Exception ex, string fallbackMessage)
    {
        if (ex is HttpRequestException or SocketException)
        {
            return "Unable to reach authentication services. Please check your internet connection and try again.";
        }

        if (ex is TaskCanceledException or TimeoutException)
        {
            return "The connection timed out. Please check your network and try again.";
        }

        string msg = ex.Message;
        if (string.IsNullOrWhiteSpace(msg))
        {
            return fallbackMessage;
        }

        // Strip out raw URLs, hostnames, ports, and socket errors
        if (msg.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains(".org", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains(".net", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("proxy", StringComparison.OrdinalIgnoreCase))
        {
            return "Unable to reach authentication services. Please check your internet connection and try again.";
        }

        return msg;
    }
}

public class CloudBackupSettingsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly IEnumerable<ICloudBackupProvider> _providers;
    private readonly IDialogService _dialogService;

    public ObservableCollection<CloudProviderViewModel> ProviderViewModels { get; } = new();

    private bool _enableCloudBackups;
    public bool EnableCloudBackups
    {
        get => _enableCloudBackups;
        set
        {
            if (SetProperty(ref _enableCloudBackups, value)) SaveSettings();
        }
    }

    private bool _uploadOnManualBackup;
    public bool UploadOnManualBackup
    {
        get => _uploadOnManualBackup;
        set
        {
            if (SetProperty(ref _uploadOnManualBackup, value)) SaveSettings();
        }
    }

    private bool _uploadOnScheduledBackup;
    public bool UploadOnScheduledBackup
    {
        get => _uploadOnScheduledBackup;
        set
        {
            if (SetProperty(ref _uploadOnScheduledBackup, value)) SaveSettings();
        }
    }

    public CloudBackupSettingsViewModel(SettingsManager settingsManager, IEnumerable<ICloudBackupProvider> providers, IDialogService dialogService)
    {
        _settingsManager = settingsManager;
        _providers = providers;
        _dialogService = dialogService;
        LoadSettings();
        InitializeProviders();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Load();
        _enableCloudBackups = settings.CloudBackups.EnableCloudBackups;
        _uploadOnManualBackup = settings.CloudBackups.UploadOnManualBackup;
        _uploadOnScheduledBackup = settings.CloudBackups.UploadOnScheduledBackup;
    }

    private void SaveSettings()
    {
        var settings = _settingsManager.Load();
        settings.CloudBackups.EnableCloudBackups = _enableCloudBackups;
        settings.CloudBackups.UploadOnManualBackup = _uploadOnManualBackup;
        settings.CloudBackups.UploadOnScheduledBackup = _uploadOnScheduledBackup;
        _settingsManager.Save(settings);
    }

    private void InitializeProviders()
    {
        foreach (var provider in _providers)
        {
            var vm = new CloudProviderViewModel(provider, _dialogService);
            ProviderViewModels.Add(vm);
            _ = vm.RefreshStatusAsync();
        }
    }
}
