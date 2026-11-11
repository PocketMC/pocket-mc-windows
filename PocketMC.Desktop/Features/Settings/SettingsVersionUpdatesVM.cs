using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Configuration;
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
    private readonly Func<bool, Task>? _onUpdateCompleted;
    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private string _serverDir;

    private MinecraftVersion? _selectedTargetVersion;
    private ModLoaderVersion? _selectedTargetLoaderVersion;
    private bool _isBusy;
    private bool _isLoadingTargetVersions;
    private bool _suppressLoaderReload;
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
        ApplicationState applicationState,
        SettingsManager settingsManager,
        Func<bool, Task>? onUpdateCompleted = null)
    {
        _metadata = metadata;
        _serverDir = serverDir;
        _updateService = updateService;
        _versionTargetService = versionTargetService;
        _dialogService = dialogService;
        _isRunningCheck = isRunningCheck;
        _javaProvisioningService = javaProvisioningService;
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _onUpdateCompleted = onUpdateCompleted;

        _targetVersionStatusText = L.T("Checking for available updates...");

        LoadTargetVersionsCommand = new AsyncRelayCommand(_ => LoadTargetVersionsAsync(), _ => !IsBusy && !IsLoadingTargetVersions);
        ApplyMinecraftUpdateCommand = new AsyncRelayCommand(_ => ApplyMinecraftUpdateAsync(), _ => CanApplyMinecraftUpdate);
        ApplyLoaderUpdateCommand = new AsyncRelayCommand(_ => ApplyLoaderUpdateAsync(), _ => CanApplyLoaderUpdate);

        _ = LoadTargetVersionsAsync();
    }

    public ObservableCollection<MinecraftVersion> TargetVersions { get; } = new();
    public ObservableCollection<ModLoaderVersion> TargetLoaderVersions { get; } = new();

    public ICommand LoadTargetVersionsCommand { get; }
    public ICommand ApplyMinecraftUpdateCommand { get; }
    public ICommand ApplyLoaderUpdateCommand { get; }

    public string CurrentServerVersion => _metadata.MinecraftVersion;
    public string CurrentLoaderVersion => _metadata.LoaderVersion ?? "Unknown";
    public string CurrentServerType => _metadata.ServerType;
    public bool IsUpdateSupported => !_metadata.IsModpack;

    public string TargetMinecraftVersion => SelectedTargetVersion?.Id ?? string.Empty;
    public string TargetLoaderVersionId => SelectedTargetLoaderVersion?.Version ?? string.Empty;
    public bool HasTargetVersions => TargetVersions.Count > 0;
    public bool HasTargetLoaderVersions => TargetLoaderVersions.Count > 0;

    public MinecraftVersion? SelectedTargetVersion
    {
        get => _selectedTargetVersion;
        set
        {
            if (!SetProperty(ref _selectedTargetVersion, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TargetMinecraftVersion));
            OnPropertyChanged(nameof(CanApplyMinecraftUpdate));
            OnPropertyChanged(nameof(CanApplyLoaderUpdate));
            StatusText = L.T("Ready to update.");

            if (!_suppressLoaderReload && !IsLoadingTargetVersions)
            {
                // Child list follows the parent picker without a second parent selection.
                _ = LoadLoaderBuildsAsync();
            }
        }
    }

    public ModLoaderVersion? SelectedTargetLoaderVersion
    {
        get => _selectedTargetLoaderVersion;
        set
        {
            if (SetProperty(ref _selectedTargetLoaderVersion, value))
            {
                OnPropertyChanged(nameof(TargetLoaderVersionId));
                OnPropertyChanged(nameof(CanApplyMinecraftUpdate));
                OnPropertyChanged(nameof(CanApplyLoaderUpdate));
                StatusText = value == null && SelectedTargetVersion == null
                    ? L.T("No target update selected.")
                    : L.T("Ready to update.");
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool UseDownloadMirrors
    {
        get => _applicationState.Settings.UseDownloadMirrors;
        set
        {
            var settings = _applicationState.Settings;
            if (settings.UseDownloadMirrors == value)
            {
                return;
            }

            bool previous = settings.UseDownloadMirrors;
            settings.UseDownloadMirrors = value;
            if (!TrySaveSettings(() => settings.UseDownloadMirrors = previous))
            {
                return;
            }

            OnPropertyChanged();
        }
    }

    public string CustomDownloadMirror
    {
        get => _applicationState.Settings.CustomDownloadMirror ?? string.Empty;
        set
        {
            string normalized = (value ?? string.Empty).Trim();
            var settings = _applicationState.Settings;
            if (string.Equals(settings.CustomDownloadMirror, normalized, StringComparison.Ordinal))
            {
                return;
            }

            string? previous = settings.CustomDownloadMirror;
            settings.CustomDownloadMirror = normalized.Length == 0 ? null : normalized;
            if (!TrySaveSettings(() => settings.CustomDownloadMirror = previous))
            {
                OnPropertyChanged();
                return;
            }

            OnPropertyChanged();
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

    public bool IsEmptyStateVisible => !IsLoadingTargetVersions && !HasTargetVersions && !HasTargetLoaderVersions;
    public bool IsMainContentVisible => IsLoadingTargetVersions || HasTargetVersions || HasTargetLoaderVersions;

    public bool CanApplyMinecraftUpdate =>
        !IsBusy &&
        !IsLoadingTargetVersions &&
        !_isRunningCheck() &&
        SelectedTargetVersion != null &&
        (!string.Equals(CurrentServerVersion, TargetMinecraftVersion, StringComparison.OrdinalIgnoreCase) ||
         IsLoaderChangeSelected);

    public bool CanApplyLoaderUpdate =>
        !IsBusy &&
        !IsLoadingTargetVersions &&
        !_isRunningCheck() &&
        SelectedTargetLoaderVersion != null &&
        (IsLoaderChangeSelected ||
         !string.Equals(CurrentServerVersion, TargetMinecraftVersion, StringComparison.OrdinalIgnoreCase));

    private bool IsLoaderChangeSelected =>
        !string.IsNullOrWhiteSpace(TargetLoaderVersionId) &&
        !string.Equals(CurrentLoaderVersion, TargetLoaderVersionId, StringComparison.OrdinalIgnoreCase);

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
        TargetVersionStatusText = L.F("Checking available {0} versions...", CurrentServerType);
        StatusText = L.T("Loading target version...");

        try
        {
            IReadOnlyList<MinecraftVersion> targets = await _versionTargetService.GetAvailableTargetVersionsAsync(_metadata);

            _suppressLoaderReload = true;
            try
            {
                TargetVersions.Clear();
                foreach (MinecraftVersion version in targets)
                {
                    TargetVersions.Add(version);
                }

                SelectedTargetVersion = TargetVersions.FirstOrDefault(version => IsSameVersion(version.Id, CurrentServerVersion))
                    ?? TargetVersions.FirstOrDefault();
                SelectedTargetLoaderVersion = null;
            }
            finally
            {
                _suppressLoaderReload = false;
            }

            OnPropertyChanged(nameof(HasTargetVersions));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsMainContentVisible));

            await LoadLoaderBuildsAsync();

            TargetVersionStatusText = TargetVersions.Count == 0
                ? L.F("No {0} versions are available.", CurrentServerType)
                : L.F("{0} {1} version(s) available. Current: {2}.", TargetVersions.Count, CurrentServerType, CurrentServerVersion);
            StatusText = SelectedTargetVersion == null && SelectedTargetLoaderVersion == null
                ? L.T("No target update selected.")
                : L.T("Ready to update.");
        }
        catch (Exception ex)
        {
            TargetVersions.Clear();
            TargetLoaderVersions.Clear();
            SelectedTargetVersion = null;
            SelectedTargetLoaderVersion = null;
            OnPropertyChanged(nameof(HasTargetVersions));
            OnPropertyChanged(nameof(HasTargetLoaderVersions));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsMainContentVisible));
            TargetVersionStatusText = L.T("Could not load server versions.");
            StatusText = L.T("Version lookup failed.");
            _dialogService.ShowMessage(L.T("Version Lookup Failed"), ex.Message, DialogType.Warning);
        }
        finally
        {
            IsLoadingTargetVersions = false;
            OnPropertyChanged(nameof(CanApplyMinecraftUpdate));
            OnPropertyChanged(nameof(CanApplyLoaderUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task LoadLoaderBuildsAsync()
    {
        MinecraftVersion? gameVersion = SelectedTargetVersion;
        if (gameVersion == null)
        {
            TargetLoaderVersions.Clear();
            SelectedTargetLoaderVersion = null;
            OnPropertyChanged(nameof(HasTargetLoaderVersions));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsMainContentVisible));
            return;
        }

        try
        {
            IReadOnlyList<ModLoaderVersion> builds = await _versionTargetService.GetAvailableLoaderVersionsAsync(_metadata, gameVersion.Id);

            // A fast picker switch may finish out of order; ignore stale results.
            if (!ReferenceEquals(SelectedTargetVersion, gameVersion))
            {
                return;
            }

            TargetLoaderVersions.Clear();
            foreach (ModLoaderVersion build in builds)
            {
                TargetLoaderVersions.Add(build);
            }

            SelectedTargetLoaderVersion = TargetLoaderVersions.FirstOrDefault(build => string.Equals(build.Version, CurrentLoaderVersion, StringComparison.OrdinalIgnoreCase))
                ?? TargetLoaderVersions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            TargetLoaderVersions.Clear();
            SelectedTargetLoaderVersion = null;
            _dialogService.ShowMessage(L.T("Version Lookup Failed"), ex.Message, DialogType.Warning);
        }
        finally
        {
            OnPropertyChanged(nameof(HasTargetLoaderVersions));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsMainContentVisible));
            OnPropertyChanged(nameof(CanApplyMinecraftUpdate));
            OnPropertyChanged(nameof(CanApplyLoaderUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ApplyMinecraftUpdateAsync()
    {
        string targetMcVersion = SelectedTargetVersion != null ? TargetMinecraftVersion.Trim() : CurrentServerVersion;
        string? targetLoaderVersion = string.IsNullOrWhiteSpace(TargetLoaderVersionId) ? null : TargetLoaderVersionId.Trim();

        DialogResult confirm = await _dialogService.ShowDialogAsync(
            L.T("Apply Update"),
            targetLoaderVersion == null
                ? L.F("Update this server to {0} {1}?\n\nPocketMC will download the necessary files.", CurrentServerType, targetMcVersion)
                : L.F("Update this server to {0} with loader {1}?\n\nPocketMC will download the necessary files.", targetMcVersion, targetLoaderVersion),
            DialogType.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await ApplyUpdateInternalAsync(targetMcVersion, targetLoaderVersion, true);
    }

    private async Task ApplyLoaderUpdateAsync()
    {
        string targetLoaderVersion = SelectedTargetLoaderVersion != null ? TargetLoaderVersionId.Trim() : CurrentLoaderVersion;
        string targetMcVersion = string.IsNullOrWhiteSpace(TargetMinecraftVersion) ? CurrentServerVersion : TargetMinecraftVersion;

        DialogResult confirm = await _dialogService.ShowDialogAsync(
            L.T("Apply Update"),
            L.F("Update this server's loader to {0}?\n\nPocketMC will download the necessary files.", targetLoaderVersion),
            DialogType.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await ApplyUpdateInternalAsync(targetMcVersion, targetLoaderVersion, false);
    }

    private async Task ApplyUpdateInternalAsync(string targetMcVersion, string? targetLoaderVersion, bool isMinecraftUpdate)
    {
        IsBusy = true;
        IsUpdateProgressVisible = true;
        IsUpdateProgressIndeterminate = true;
        UpdateProgressValue = 0;
        ProgressDetailText = L.T("Preparing update.");
        try
        {
            StatusText = L.T("Downloading update...");
            var downloadProgress = new Progress<DownloadProgress>(progress =>
            {
                IsUpdateProgressIndeterminate = progress.TotalBytes <= 0;
                UpdateProgressValue = progress.TotalBytes > 0 ? Math.Clamp(progress.Percentage, 0, 100) : 0;
                ProgressDetailText = progress.TotalBytes > 0
                    ? $"{FormatMegabytes(progress.BytesRead)} / {FormatMegabytes(progress.TotalBytes)} downloaded"
                    : $"{FormatMegabytes(progress.BytesRead)} downloaded";
            });

            var result = await _updateService.UpdateAsync(
                _serverDir,
                _metadata,
                targetMcVersion,
                targetLoaderVersion: targetLoaderVersion,
                progress: downloadProgress,
                onProgress: message =>
                {
                    StatusText = message;
                    ProgressDetailText = message;
                });

            _metadata.MinecraftVersion = result.UpdatedMetadata.MinecraftVersion;
            _metadata.LoaderVersion = result.UpdatedMetadata.LoaderVersion;
            OnPropertyChanged(nameof(CurrentServerVersion));
            OnPropertyChanged(nameof(CurrentLoaderVersion));
            UpdateProgressValue = 100;
            IsUpdateProgressIndeterminate = false;
            ProgressDetailText = L.T("Update completed successfully.");
            if (_onUpdateCompleted != null)
            {
                StatusText = L.T("Checking for addon updates...");
                ProgressDetailText = L.T("Looking for addon updates...");
                await _onUpdateCompleted.Invoke(isMinecraftUpdate);
            }

            _dialogService.ShowMessage(L.T("Update Complete"), L.T("The server update completed successfully."));
            await LoadTargetVersionsAsync();
            StatusText = string.Empty;
            ProgressDetailText = string.Empty;
            IsUpdateProgressVisible = false;
        }
        catch (Exception ex)
        {
            IsUpdateProgressIndeterminate = false;
            ProgressDetailText = L.T("Update failed.");
            StatusText = L.T("Update failed.");
            _dialogService.ShowMessage(L.T("Update Failed"), ex.Message, DialogType.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApplyMinecraftUpdate));
            OnPropertyChanged(nameof(CanApplyLoaderUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool TrySaveSettings(Action rollback)
    {
        try
        {
            _settingsManager.Save(_applicationState.Settings);
            return true;
        }
        catch (Exception ex)
        {
            rollback();
            _dialogService.ShowMessage(L.T("Settings Error"), ex.Message, DialogType.Error);
            return false;
        }
    }

    private static bool IsSameVersion(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMegabytes(long bytes)
    {
        return $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }
}
