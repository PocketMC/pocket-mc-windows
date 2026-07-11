using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Settings.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Java;

using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Settings.ViewModels;

public sealed class SettingsVersionUpdatesViewModel : ViewModelBase
{
    private readonly InstanceMetadata _metadata;
    private readonly InstanceUpdateService _updateService;
    private readonly InstanceVersionTargetService _versionTargetService;
    private readonly InstanceUpdateJournalStore _journalStore;
    private readonly InstanceRollbackService _rollbackService;
    private readonly IDialogService _dialogService;
    private readonly Func<bool> _isRunningCheck;
    private readonly Action _onReloadRequested;
    private string _serverDir;
    private InstanceUpdatePlan? _currentPlan;

    private MinecraftVersion? _selectedTargetVersion;
    private UpdateModeOption _selectedUpdateMode;
    private bool _isBusy;
    private bool _isLoadingTargetVersions;
    private bool _isUpdateProgressVisible;
    private bool _isUpdateProgressIndeterminate;
    private double _updateProgressValue;
    private string _statusText = "";
    private string _targetVersionStatusText = "";
    private string _progressDetailText = "";
    private string _requiredJavaVersionChange = "";
    private int _trackedAddonCount;
    private int _manualUntrackedAddonCount;
    private int _compatibleUpdateCount;
    private int _incompatibleAddonCount;
    private int _dependencyAdditionCount;
    private string _changelogPreview = "";
    private string _rollbackAvailabilityText = "Checking...";

    public SettingsVersionUpdatesViewModel(
        InstanceMetadata metadata,
        string serverDir,
        InstanceUpdateService updateService,
        InstanceVersionTargetService versionTargetService,
        InstanceUpdateJournalStore journalStore,
        InstanceRollbackService rollbackService,
        IDialogService dialogService,
        Func<bool> isRunningCheck,
        Action onReloadRequested)
    {
        _metadata = metadata;
        _serverDir = serverDir;
        _updateService = updateService;
        _versionTargetService = versionTargetService;
        _journalStore = journalStore;
        _rollbackService = rollbackService;
        _dialogService = dialogService;
        _isRunningCheck = isRunningCheck;
        _onReloadRequested = onReloadRequested;
        int currentJava = JavaRuntimeResolver.GetRequiredJavaVersion(metadata);
        _requiredJavaVersionChange = $"Java {currentJava} remains required";
        _targetVersionStatusText = "Checking for available updates...";
        _changelogPreview = $"Current {metadata.ServerType} version is {metadata.MinecraftVersion}. Select an available target version and preview the update.";

        UpdateModes =
        [
            new UpdateModeOption("Server + compatible marketplace addons", InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons),
            new UpdateModeOption("Server only, warn about addons", InstanceUpdateMode.ServerOnlyWarnAboutAddons),
            new UpdateModeOption("Server + compatible marketplace addons + dependencies", InstanceUpdateMode.ServerAndCompatibleMarketplaceAddonsAndDependencies),
            new UpdateModeOption("Experimental aggressive update", InstanceUpdateMode.ExperimentalAggressiveUpdate)
        ];
        _selectedUpdateMode = UpdateModes[0];

        LoadTargetVersionsCommand = new AsyncRelayCommand(_ => LoadTargetVersionsAsync(), _ => !IsBusy && !IsLoadingTargetVersions);
        PlanCommand = new AsyncRelayCommand(_ => RefreshPlanAsync(), _ => CanPreviewUpdate);
        ApplyCommand = new AsyncRelayCommand(_ => ApplyAsync(), _ => CanApplyUpdate);
        RollbackCommand = new AsyncRelayCommand(_ => RollbackAsync(), _ => !IsBusy && !_isRunningCheck() && HasRollbackBackup);
        DeleteRollbackCommand = new AsyncRelayCommand(_ => DeleteRollbackBackupAsync(), _ => !IsBusy && HasRollbackBackup);

        _ = LoadTargetVersionsAsync();
        _ = RefreshRollbackAvailabilityAsync();
    }

    public ObservableCollection<UpdateModeOption> UpdateModes { get; }
    public ObservableCollection<MinecraftVersion> TargetVersions { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();

    public ICommand LoadTargetVersionsCommand { get; }
    public ICommand PlanCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand RollbackCommand { get; }
    public ICommand DeleteRollbackCommand { get; }

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
                _currentPlan = null;
                OnPropertyChanged(nameof(TargetMinecraftVersion));
                OnPropertyChanged(nameof(CanPreviewUpdate));
                OnPropertyChanged(nameof(CanApplyUpdate));
                StatusText = value == null
                    ? "No target update selected."
                    : $"Ready to preview update to {value.Id}.";
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public UpdateModeOption SelectedUpdateMode
    {
        get => _selectedUpdateMode;
        set
        {
            if (SetProperty(ref _selectedUpdateMode, value))
            {
                _currentPlan = null;
                OnPropertyChanged(nameof(CanApplyUpdate));
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
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanPreviewUpdate =>
        !IsBusy &&
        !IsLoadingTargetVersions &&
        !_isRunningCheck() &&
        SelectedTargetVersion != null;

    public bool CanApplyUpdate =>
        !IsBusy &&
        !IsLoadingTargetVersions &&
        !_isRunningCheck() &&
        _currentPlan != null &&
        !string.Equals(CurrentServerVersion, TargetMinecraftVersion, StringComparison.OrdinalIgnoreCase);

    private bool _hasRollbackBackup;
    public bool HasRollbackBackup { get => _hasRollbackBackup; set => SetProperty(ref _hasRollbackBackup, value); }

    public bool IsUpdateProgressVisible { get => _isUpdateProgressVisible; set => SetProperty(ref _isUpdateProgressVisible, value); }
    public bool IsUpdateProgressIndeterminate { get => _isUpdateProgressIndeterminate; set => SetProperty(ref _isUpdateProgressIndeterminate, value); }
    public double UpdateProgressValue { get => _updateProgressValue; set => SetProperty(ref _updateProgressValue, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string TargetVersionStatusText { get => _targetVersionStatusText; set => SetProperty(ref _targetVersionStatusText, value); }
    public string ProgressDetailText { get => _progressDetailText; set => SetProperty(ref _progressDetailText, value); }
    public string RequiredJavaVersionChange { get => _requiredJavaVersionChange; set => SetProperty(ref _requiredJavaVersionChange, value); }
    public int TrackedAddonCount { get => _trackedAddonCount; set => SetProperty(ref _trackedAddonCount, value); }
    public int ManualUntrackedAddonCount { get => _manualUntrackedAddonCount; set => SetProperty(ref _manualUntrackedAddonCount, value); }
    public int CompatibleUpdateCount { get => _compatibleUpdateCount; set => SetProperty(ref _compatibleUpdateCount, value); }
    public int IncompatibleAddonCount { get => _incompatibleAddonCount; set => SetProperty(ref _incompatibleAddonCount, value); }
    public int DependencyAdditionCount { get => _dependencyAdditionCount; set => SetProperty(ref _dependencyAdditionCount, value); }
    public string ChangelogPreview { get => _changelogPreview; set => SetProperty(ref _changelogPreview, value); }
    public string RollbackAvailabilityText { get => _rollbackAvailabilityText; set => SetProperty(ref _rollbackAvailabilityText, value); }

    public void UpdateServerDir(string newDir)
    {
        if (_serverDir == newDir)
        {
            return;
        }

        _serverDir = newDir;
        _currentPlan = null;
        _ = LoadTargetVersionsAsync();
        _ = RefreshRollbackAvailabilityAsync();
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
            SelectedTargetVersion = TargetVersions.FirstOrDefault();
            TargetVersionStatusText = SelectedTargetVersion == null
                ? $"No newer {CurrentServerType} release is available after {CurrentServerVersion}."
                : $"{TargetVersions.Count} available {CurrentServerType} update(s) after {CurrentServerVersion}. Newest: {SelectedTargetVersion.Id}.";
            StatusText = SelectedTargetVersion == null
                ? "No newer target version found."
                : $"Ready to preview update to {SelectedTargetVersion.Id}.";
        }
        catch (Exception ex)
        {
            TargetVersions.Clear();
            SelectedTargetVersion = null;
            OnPropertyChanged(nameof(HasTargetVersions));
            TargetVersionStatusText = "Could not load server versions.";
            StatusText = "Version lookup failed.";
            _dialogService.ShowMessage("Version Lookup Failed", ex.Message, DialogType.Warning);
        }
        finally
        {
            IsLoadingTargetVersions = false;
            OnPropertyChanged(nameof(CanPreviewUpdate));
            OnPropertyChanged(nameof(CanApplyUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RefreshPlanAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetMinecraftVersion))
        {
            _dialogService.ShowMessage("Target Version Required", "Select a target Minecraft version before previewing the update.", DialogType.Warning);
            return;
        }

        IsBusy = true;
        IsUpdateProgressVisible = false;
        StatusText = "Building update plan...";
        try
        {
            _currentPlan = await _updateService.PlanAsync(
                _serverDir,
                _metadata,
                TargetMinecraftVersion.Trim(),
                SelectedUpdateMode.Mode);

            RequiredJavaVersionChange = _currentPlan.RequiredJavaVersionChangeText;
            ChangelogPreview = _currentPlan.ChangelogPreview;
            TrackedAddonCount = _currentPlan.AddonMigrationPlan.TrackedAddonCount;
            ManualUntrackedAddonCount = _currentPlan.AddonMigrationPlan.ManualUntrackedAddonCount;
            CompatibleUpdateCount = _currentPlan.AddonMigrationPlan.CompatibleUpdateCount;
            IncompatibleAddonCount = _currentPlan.AddonMigrationPlan.IncompatibleAddonCount;
            DependencyAdditionCount = _currentPlan.AddonMigrationPlan.DependencyAdditionCount;
            Warnings.Clear();
            foreach (AddonMigrationWarning warning in _currentPlan.AddonMigrationPlan.Warnings)
            {
                Warnings.Add(warning.Message);
            }

            RollbackAvailabilityText = _currentPlan.RollbackAvailable
                ? "Rollback is available from a previous incomplete update."
                : "No pending rollback is available.";
            StatusText = "Update plan ready.";
            OnPropertyChanged(nameof(CanApplyUpdate));
        }
        catch (Exception ex)
        {
            StatusText = "Planning failed.";
            _dialogService.ShowMessage("Update Planning Failed", ex.Message, DialogType.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanPreviewUpdate));
            OnPropertyChanged(nameof(CanApplyUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ApplyAsync()
    {
        if (_currentPlan == null)
        {
            await RefreshPlanAsync();
            if (_currentPlan == null)
            {
                return;
            }
        }

        DialogResult confirm = await _dialogService.ShowDialogAsync(
            "Apply Version Update",
            $"Update this server from {CurrentServerVersion} to {TargetMinecraftVersion}?\n\nPocketMC will stage artifacts, snapshot the instance, run a world backup, then apply the update with rollback support.",
            DialogType.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        IsBusy = true;
        IsUpdateProgressVisible = true;
        IsUpdateProgressIndeterminate = true;
        UpdateProgressValue = 0;
        ProgressDetailText = "Preparing staging folders.";
        try
        {
            StatusText = "Staging update artifacts...";
            var downloadProgress = new Progress<DownloadProgress>(progress =>
            {
                IsUpdateProgressIndeterminate = progress.TotalBytes <= 0;
                UpdateProgressValue = progress.TotalBytes > 0 ? Math.Clamp(progress.Percentage, 0, 100) : 0;
                ProgressDetailText = progress.TotalBytes > 0
                    ? $"{FormatMegabytes(progress.BytesRead)} / {FormatMegabytes(progress.TotalBytes)} downloaded"
                    : $"{FormatMegabytes(progress.BytesRead)} downloaded";
            });

            InstanceUpdateStagedArtifacts staged = await _updateService.StageAsync(_currentPlan, downloadProgress);
            UpdateProgressValue = 50;
            IsUpdateProgressIndeterminate = true;
            ProgressDetailText = "Artifacts staged. Creating snapshot, backup, and applying changes.";
            StatusText = "Applying update...";
            await _updateService.ApplyAsync(_currentPlan, staged, message =>
            {
                StatusText = message;
                ProgressDetailText = message;
            });
            _metadata.MinecraftVersion = TargetMinecraftVersion.Trim();
            OnPropertyChanged(nameof(CurrentServerVersion));
            UpdateProgressValue = 100;
            IsUpdateProgressIndeterminate = false;
            ProgressDetailText = "Update completed successfully.";
            StatusText = "Update complete.";
            _dialogService.ShowMessage("Update Complete", "The server update completed successfully.");
            _currentPlan = null;
            await LoadTargetVersionsAsync();
            await RefreshRollbackAvailabilityAsync();
            StatusText = "Update complete.";
            ProgressDetailText = "Update completed successfully.";
        }
        catch (Exception ex)
        {
            IsUpdateProgressIndeterminate = false;
            ProgressDetailText = "Update failed. Rollback was attempted.";
            StatusText = "Update failed and rollback was attempted.";
            _dialogService.ShowMessage("Update Failed", ex.Message, DialogType.Error);
            await RefreshRollbackAvailabilityAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanPreviewUpdate));
            OnPropertyChanged(nameof(CanApplyUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RollbackAsync()
    {
        DialogResult confirm = await _dialogService.ShowDialogAsync(
            "Rollback Update",
            "Rollback the server to the pre-upgrade backup?",
            DialogType.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        IsBusy = true;
        IsUpdateProgressVisible = true;
        IsUpdateProgressIndeterminate = true;
        ProgressDetailText = "Restoring rollback backup.";
        try
        {
            bool rolledBack = await _updateService.RollbackLatestAsync(_metadata.Id, _serverDir);
            StatusText = rolledBack ? "Rollback completed." : "No rollback backup was found.";
            ProgressDetailText = StatusText;
            IsUpdateProgressIndeterminate = false;
            UpdateProgressValue = rolledBack ? 100 : 0;
            await RefreshRollbackAvailabilityAsync();

            if (rolledBack)
            {
                _dialogService.ShowMessage("Rollback Successful", "The server has been rolled back. The settings page will now reload to reflect the restored configuration.", DialogType.Information);
                _onReloadRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Rollback failed.";
            _dialogService.ShowMessage("Rollback Failed", ex.Message, DialogType.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanPreviewUpdate));
            OnPropertyChanged(nameof(CanApplyUpdate));
        }
    }

    private async Task DeleteRollbackBackupAsync()
    {
        DialogResult confirm = await _dialogService.ShowDialogAsync(
            "Delete Rollback Backup",
            "Are you sure you want to permanently delete the rollback backup to free disk space? This cannot be undone.",
            DialogType.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Deleting rollback backup...";
        try
        {
            await _rollbackService.DeleteRollbackBackupAsync(_serverDir);
            StatusText = "Rollback backup deleted.";
            await RefreshRollbackAvailabilityAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Failed to delete rollback backup.";
            _dialogService.ShowMessage("Delete Failed", ex.Message, DialogType.Error);
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RefreshRollbackAvailabilityAsync()
    {
        HasRollbackBackup = _rollbackService.HasRollbackBackup(_serverDir);
        RollbackAvailabilityText = HasRollbackBackup
            ? "Rollback backup is available."
            : "No rollback backup exists.";

        CommandManager.InvalidateRequerySuggested();
        await Task.CompletedTask;
    }

    private static string FormatMegabytes(long bytes)
    {
        return $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }
}

public sealed class UpdateModeOption
{
    public UpdateModeOption(string displayName, InstanceUpdateMode mode)
    {
        DisplayName = displayName;
        Mode = mode;
    }

    public string DisplayName { get; }
    public InstanceUpdateMode Mode { get; }
}
