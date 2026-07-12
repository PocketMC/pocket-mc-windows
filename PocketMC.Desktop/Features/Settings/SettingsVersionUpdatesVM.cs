using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Instances.Updates;

namespace PocketMC.Desktop.Features.Settings;

public sealed class SettingsVersionUpdatesVM : ViewModelBase
{
    private readonly InstanceMetadata _metadata;
    private readonly InstanceUpdateService _updateService;
    private readonly InstanceVersionTargetService _versionTargetService;
    private readonly IDialogService _dialogService;
    private readonly Func<bool> _isRunningCheck;
    private readonly JavaProvisioningService _javaProvisioningService;
    private readonly Func<Task>? _onUpdateCompleted;
    private string _serverDir;

    private MinecraftVersion? _selectedTargetVersion;
    private bool _isBusy;
    private bool _isLoadingTargetVersions;
    private bool _isUpdateProgressVisible;
    private bool _isUpdateProgressIndeterminate;
    private double _updateProgressValue;
    private string _statusText = "";
    private string _targetVersionStatusText = "";
    private string _progressDetailText = "";

    public SettingsVersionUpdatesVM(
        InstanceMetadata metadata,
        string serverDir,
        InstanceUpdateService updateService,
        InstanceVersionTargetService versionTargetService,
        IDialogService dialogService,
        Func<bool> isRunningCheck,
        JavaProvisioningService javaProvisioningService,
        Func<Task>? onUpdateCompleted = null)
    {
        _metadata = metadata;
        _serverDir = serverDir;
        _updateService = updateService;
        _versionTargetService = versionTargetService;
        _dialogService = dialogService;
        _isRunningCheck = isRunningCheck;
        _javaProvisioningService = javaProvisioningService;
        _onUpdateCompleted = onUpdateCompleted;
        
        _targetVersionStatusText = "Checking for available updates...";

        LoadTargetVersionsCommand = new AsyncRelayCommand(_ => LoadTargetVersionsAsync(), _ => !IsBusy && !IsLoadingTargetVersions);
        ApplyCommand = new AsyncRelayCommand(_ => ApplyAsync(), _ => CanApplyUpdate);

        _ = LoadTargetVersionsAsync();
    }

    public ObservableCollection<MinecraftVersion> TargetVersions { get; } = new();

    public ICommand LoadTargetVersionsCommand { get; }
    public ICommand ApplyCommand { get; }

    public string CurrentServerVersion => _metadata.MinecraftVersion;
    public string CurrentServerType => _metadata.ServerType;
    public bool IsUpdateSupported => !_metadata.IsModpack;

    public string TargetMinecraftVersion => SelectedTargetVersion?.Id ?? string.Empty;
    public bool HasTargetVersions => TargetVersions.Count > 0;

    public MinecraftVersion? SelectedTargetVersion
    {
        get => _selectedTargetVersion;
        set
        {
            if (SetProperty(ref _selectedTargetVersion, value))
            {
                OnPropertyChanged(nameof(TargetMinecraftVersion));
                OnPropertyChanged(nameof(CanApplyUpdate));
                StatusText = value == null
                    ? "No target update selected."
                    : $"Ready to update to {value.Id}.";
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsLoadingTargetVersions
    {
        get => _isLoadingTargetVersions;
        set
        {
            if (SetProperty(ref _isLoadingTargetVersions, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(IsMainContentVisible));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsEmptyStateVisible => !IsLoadingTargetVersions && !HasTargetVersions;
    public bool IsMainContentVisible => IsLoadingTargetVersions || HasTargetVersions;

    public bool CanApplyUpdate =>
        !IsBusy &&
        !IsLoadingTargetVersions &&
        !_isRunningCheck() &&
        SelectedTargetVersion != null &&
        !string.Equals(CurrentServerVersion, TargetMinecraftVersion, StringComparison.OrdinalIgnoreCase);

    public bool IsUpdateProgressVisible { get => _isUpdateProgressVisible; set => SetProperty(ref _isUpdateProgressVisible, value); }
    public bool IsUpdateProgressIndeterminate { get => _isUpdateProgressIndeterminate; set => SetProperty(ref _isUpdateProgressIndeterminate, value); }
    public double UpdateProgressValue { get => _updateProgressValue; set => SetProperty(ref _updateProgressValue, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string TargetVersionStatusText { get => _targetVersionStatusText; set => SetProperty(ref _targetVersionStatusText, value); }
    public string ProgressDetailText { get => _progressDetailText; set => SetProperty(ref _progressDetailText, value); }

    public void UpdateServerDir(string newDir)
    {
        if (_serverDir == newDir)
        {
            return;
        }

        _serverDir = newDir;
        _ = LoadTargetVersionsAsync();
    }

    private async Task LoadTargetVersionsAsync()
    {
        IsLoadingTargetVersions = true;
        TargetVersionStatusText = $"Checking available {CurrentServerType} updates...";
        StatusText = "Loading target version...";

        try
        {
            IReadOnlyList<MinecraftVersion> targets = await _versionTargetService.GetAvailableTargetVersionsAsync(_metadata);

            TargetVersions.Clear();
            foreach (MinecraftVersion version in targets)
            {
                TargetVersions.Add(version);
            }

            OnPropertyChanged(nameof(HasTargetVersions));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsMainContentVisible));
            SelectedTargetVersion = TargetVersions.FirstOrDefault();
            TargetVersionStatusText = SelectedTargetVersion == null
                ? $"No newer {CurrentServerType} release is available after {CurrentServerVersion}."
                : $"{TargetVersions.Count} available {CurrentServerType} update(s) after {CurrentServerVersion}. Newest: {SelectedTargetVersion.Id}.";
            StatusText = SelectedTargetVersion == null
                ? "No newer target version found."
                : $"Ready to update to {SelectedTargetVersion.Id}.";
        }
        catch (Exception ex)
        {
            TargetVersions.Clear();
            SelectedTargetVersion = null;
            OnPropertyChanged(nameof(HasTargetVersions));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsMainContentVisible));
            TargetVersionStatusText = "Could not load server versions.";
            StatusText = "Version lookup failed.";
            _dialogService.ShowMessage("Version Lookup Failed", ex.Message, DialogType.Warning);
        }
        finally
        {
            IsLoadingTargetVersions = false;
            OnPropertyChanged(nameof(CanApplyUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ApplyAsync()
    {
        DialogResult confirm = await _dialogService.ShowDialogAsync(
            "Apply Version Update",
            $"Update this server from {CurrentServerVersion} to {TargetMinecraftVersion}?\n\nPocketMC will download the new version and replace the current one.",
            DialogType.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        IsBusy = true;
        IsUpdateProgressVisible = true;
        IsUpdateProgressIndeterminate = true;
        UpdateProgressValue = 0;
        ProgressDetailText = "Preparing update.";
        try
        {
            StatusText = "Downloading update...";
            var downloadProgress = new Progress<DownloadProgress>(progress =>
            {
                IsUpdateProgressIndeterminate = progress.TotalBytes <= 0;
                UpdateProgressValue = progress.TotalBytes > 0 ? Math.Clamp(progress.Percentage, 0, 100) : 0;
                ProgressDetailText = progress.TotalBytes > 0
                    ? $"{FormatMegabytes(progress.BytesRead)} / {FormatMegabytes(progress.TotalBytes)} downloaded"
                    : $"{FormatMegabytes(progress.BytesRead)} downloaded";
            });

            await _updateService.UpdateAsync(
                _serverDir,
                _metadata,
                TargetMinecraftVersion.Trim(),
                targetLoaderVersion: null,
                progress: downloadProgress,
                onProgress: message =>
                {
                    StatusText = message;
                    ProgressDetailText = message;
                });
                
            _metadata.MinecraftVersion = TargetMinecraftVersion.Trim();
            OnPropertyChanged(nameof(CurrentServerVersion));
            UpdateProgressValue = 100;
            IsUpdateProgressIndeterminate = false;
            ProgressDetailText = "Update completed successfully.";
            if (_onUpdateCompleted != null)
            {
                StatusText = "Checking for addon updates...";
                ProgressDetailText = "Looking for addon updates...";
                await _onUpdateCompleted.Invoke();
            }

            _dialogService.ShowMessage("Update Complete", "The server update completed successfully.");
            await LoadTargetVersionsAsync();
            StatusText = string.Empty;
            ProgressDetailText = string.Empty;
            IsUpdateProgressVisible = false;
        }
        catch (Exception ex)
        {
            IsUpdateProgressIndeterminate = false;
            ProgressDetailText = "Update failed.";
            StatusText = "Update failed.";
            _dialogService.ShowMessage("Update Failed", ex.Message, DialogType.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApplyUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static string FormatMegabytes(long bytes)
    {
        return $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }
}
