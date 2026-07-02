using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.CloudBackups;
using PocketMC.Application.Instances.Backups;

namespace PocketMC.Desktop.Features.Settings;

public class ServerCloudBackupTargetViewModel : ViewModelBase
{
    private readonly CloudBackupTarget _targetModel;
    private readonly Action _onChanged;

    public CloudBackupProviderType ProviderType => _targetModel.Provider;

    public bool IsEnabled
    {
        get => _targetModel.Enabled;
        set
        {
            if (_targetModel.Enabled != value)
            {
                _targetModel.Enabled = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public int RetentionCount
    {
        get => _targetModel.RetentionCount ?? 5;
        set
        {
            if (_targetModel.RetentionCount != value)
            {
                _targetModel.RetentionCount = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public ServerCloudBackupTargetViewModel(CloudBackupTarget targetModel, Action onChanged)
    {
        _targetModel = targetModel;
        _onChanged = onChanged;
    }
}

public class RemoteBackupItemViewModel : ViewModelBase
{
    public CloudBackupProviderType Provider { get; set; }
    public string FileName { get; set; } = string.Empty;
    public double SizeMb { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ProviderFileId { get; set; } = string.Empty;
}

public class ServerCloudBackupViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly IReadOnlyList<ICloudBackupProvider> _providers;
    private readonly IDialogService _dialogService;
    private readonly InstanceMetadata _metadata;
    private readonly BackupService _backupService;
    private readonly Func<string> _getServerDir;
    private readonly Func<bool> _isRunningCheck;

    public ObservableCollection<ServerCloudBackupTargetViewModel> Targets { get; } = new();
    public ObservableCollection<RemoteBackupItemViewModel> RemoteBackups { get; } = new();

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isRefreshingProviders;
    public bool IsRefreshingProviders
    {
        get => _isRefreshingProviders;
        private set
        {
            if (SetProperty(ref _isRefreshingProviders, value))
            {
                OnPropertyChanged(nameof(ShowNoConnectedProvidersMessage));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private bool _isCloudBackupsEnabled;
    public bool IsCloudBackupsEnabled
    {
        get => _isCloudBackupsEnabled;
        private set
        {
            if (SetProperty(ref _isCloudBackupsEnabled, value))
            {
                OnPropertyChanged(nameof(ShowNoConnectedProvidersMessage));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private bool _hasConnectedProviders;
    public bool HasConnectedProviders
    {
        get => _hasConnectedProviders;
        private set
        {
            if (SetProperty(ref _hasConnectedProviders, value))
            {
                OnPropertyChanged(nameof(ShowNoConnectedProvidersMessage));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _providerStatusMessage = "No connected cloud providers. Connect Google Drive, Dropbox, or OneDrive in App Settings.";
    public string ProviderStatusMessage { get => _providerStatusMessage; private set => SetProperty(ref _providerStatusMessage, value); }

    public bool ShowNoConnectedProvidersMessage => IsCloudBackupsEnabled && !IsRefreshingProviders && !HasConnectedProviders;

    public ICommand RefreshRemoteBackupsCommand { get; }
    public ICommand DeleteRemoteBackupCommand { get; }
    public ICommand RestoreRemoteBackupCommand { get; }
    public Task Initialization { get; }

    public ServerCloudBackupViewModel(
        SettingsManager settingsManager,
        IEnumerable<ICloudBackupProvider> providers,
        IDialogService dialogService,
        InstanceMetadata metadata,
        BackupService backupService,
        Func<string> getServerDir,
        Func<bool> isRunningCheck)
    {
        _settingsManager = settingsManager;
        _providers = providers.ToList();
        _dialogService = dialogService;
        _metadata = metadata;
        _backupService = backupService;
        _getServerDir = getServerDir;
        _isRunningCheck = isRunningCheck;

        RefreshRemoteBackupsCommand = new RelayCommand(
            async _ => await RefreshProviderTargetsAndRemoteBackupsAsync(),
            _ => IsCloudBackupsEnabled && !IsRefreshingProviders && !IsLoading);
        DeleteRemoteBackupCommand = new RelayCommand(async p => await DeleteRemoteBackupAsync(p as RemoteBackupItemViewModel));
        RestoreRemoteBackupCommand = new RelayCommand(async p => await RestoreRemoteBackupAsync(p as RemoteBackupItemViewModel), _ => !_isRunningCheck());

        Initialization = RefreshProviderTargetsAndRemoteBackupsAsync();
    }

    private async Task RefreshProviderTargetsAndRemoteBackupsAsync()
    {
        await LoadSettingsAsync();
        if (IsCloudBackupsEnabled && HasConnectedProviders)
        {
            await LoadRemoteBackupsAsync();
        }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = _settingsManager.Load();
        IsCloudBackupsEnabled = settings.CloudBackups.EnableCloudBackups;
        Targets.Clear();
        RemoteBackups.Clear();
        HasConnectedProviders = false;

        if (!IsCloudBackupsEnabled)
        {
            ProviderStatusMessage = "Cloud backups are disabled in App Settings.";
            return;
        }

        IsRefreshingProviders = true;
        ProviderStatusMessage = "Checking connected cloud providers...";

        try
        {
            var settingsChanged = false;
            foreach (var provider in _providers)
            {
                if (!await IsProviderConnectedAsync(provider))
                {
                    continue;
                }

                var target = settings.CloudBackups.Targets.FirstOrDefault(t => t.Provider == provider.ProviderType);
                if (target == null)
                {
                    target = new CloudBackupTarget { Provider = provider.ProviderType, Enabled = false, RetentionCount = 5 };
                    settings.CloudBackups.Targets.Add(target);
                    settingsChanged = true;
                }

                Targets.Add(new ServerCloudBackupTargetViewModel(target, SaveSettings));
            }

            HasConnectedProviders = Targets.Count > 0;
            ProviderStatusMessage = HasConnectedProviders
                ? string.Empty
                : "No connected cloud providers. Connect Google Drive, Dropbox, or OneDrive in App Settings.";

            if (settingsChanged)
            {
                _settingsManager.Save(settings);
            }
        }
        finally
        {
            IsRefreshingProviders = false;
        }
    }

    private async Task<bool> IsProviderConnectedAsync(ICloudBackupProvider provider)
    {
        try
        {
            return await provider.GetStatusAsync(CancellationToken.None) == CloudBackupConnectionStatus.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void SaveSettings()
    {
        var settings = _settingsManager.Load();
        foreach (var vm in Targets)
        {
            var target = settings.CloudBackups.Targets.FirstOrDefault(t => t.Provider == vm.ProviderType);
            if (target == null)
            {
                target = new CloudBackupTarget { Provider = vm.ProviderType };
                settings.CloudBackups.Targets.Add(target);
            }

            if (target != null)
            {
                target.Enabled = vm.IsEnabled;
                target.RetentionCount = vm.RetentionCount;
            }
        }
        _settingsManager.Save(settings);
    }

    private async Task LoadRemoteBackupsAsync()
    {
        if (!IsCloudBackupsEnabled || !HasConnectedProviders)
        {
            RemoteBackups.Clear();
            return;
        }

        IsLoading = true;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        RemoteBackups.Clear();
        try
        {
            foreach (var provider in _providers)
            {
                var target = Targets.FirstOrDefault(t => t.ProviderType == provider.ProviderType);
                if (target != null && target.IsEnabled && await IsProviderConnectedAsync(provider))
                {
                    var items = await provider.ListBackupsAsync(_metadata.Id, _metadata.Name, CancellationToken.None);
                    foreach (var item in items)
                    {
                        RemoteBackups.Add(new RemoteBackupItemViewModel
                        {
                            Provider = item.Provider,
                            FileName = item.FileName,
                            SizeMb = item.SizeBytes / (1024.0 * 1024.0),
                            CreatedAt = item.CreatedUtc,
                            ProviderFileId = item.ProviderFileId
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage("Error", $"Failed to load remote backups: {ex.Message}", DialogType.Error);
        }
        finally
        {
            IsLoading = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DeleteRemoteBackupAsync(RemoteBackupItemViewModel? vm)
    {
        if (vm == null) return;

        if (await _dialogService.ShowDialogAsync("Confirm", $"Delete {vm.FileName} from {vm.Provider}?", DialogType.Question) == DialogResult.Yes)
        {
            IsLoading = true;
            try
            {
                var provider = _providers.FirstOrDefault(p => p.ProviderType == vm.Provider);
                if (provider != null)
                {
                    await provider.DeleteBackupAsync(vm.ProviderFileId, CancellationToken.None);
                    RemoteBackups.Remove(vm);
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", $"Failed to delete remote backup: {ex.Message}", DialogType.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private async Task RestoreRemoteBackupAsync(RemoteBackupItemViewModel? vm)
    {
        if (vm == null) return;

        if (await _dialogService.ShowDialogAsync("Restore Cloud Backup", $"This will download and COMPLETELY OVERWRITE current server files with the backup {vm.FileName} from {vm.Provider}. Continue?", DialogType.Warning) == DialogResult.Yes)
        {
            IsLoading = true;
            try
            {
                var provider = _providers.FirstOrDefault(p => p.ProviderType == vm.Provider);
                if (provider == null) throw new Exception("Provider not found.");

                string serverDir = _getServerDir();
                string backupsDir = Path.Combine(serverDir, "backups");
                if (!Directory.Exists(backupsDir))
                {
                    Directory.CreateDirectory(backupsDir);
                }

                string tempZipPath = Path.Combine(backupsDir, "temp_cloud_restore.zip");

                // 1. Download from cloud
                await provider.DownloadBackupAsync(vm.ProviderFileId, tempZipPath, CancellationToken.None);

                // 2. Restore using BackupService
                await _backupService.RestoreBackupAsync(_metadata, tempZipPath, serverDir);

                // 3. Clean up the downloaded zip
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }

                _dialogService.ShowMessage("Success", "Backup downloaded and restored successfully from the cloud.");
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", $"Failed to restore cloud backup: {ex.Message}", DialogType.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
