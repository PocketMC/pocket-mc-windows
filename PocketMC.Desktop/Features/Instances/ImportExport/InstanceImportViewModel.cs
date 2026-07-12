using PocketMC.Domain.Exceptions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances;

public sealed class InstanceImportViewModel : ObservableObject
{
    private readonly IInstanceImportService _importService;
    private CancellationTokenSource? _importCancellation;
    private string _zipPath = string.Empty;
    private string? _requestedName;
    private string _currentStep = "Choose an import package.";
    private double _overallProgress;
    private double _downloadProgress;
    private string? _currentItem;
    private string? _errorMessage;
    private bool _hasError;
    private bool _isImporting;
    private bool _isComplete;
    private InstanceImportResult? _importResult;
    private AddonUnavailableException? _unavailableAddon;

    private ImportMode _importMode = ImportMode.Package;
    private string _folderPath = string.Empty;
    private string _description = string.Empty;
    private string _selectedServerType = "Vanilla";
    private string _minecraftVersion = string.Empty;
    private bool _shouldCopyFiles = true;
    private bool _acceptEula = true;

    public InstanceImportViewModel(IInstanceImportService importService)
    {
        _importService = importService;
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => CanImport);
        CancelImportCommand = new RelayCommand(CancelImport, () => IsImporting);
    }

    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand CancelImportCommand { get; }

    public event EventHandler<InstanceImportResult>? ImportCompleted;
    public event EventHandler<AddonUnavailableException>? AddonUnavailable;

    public string ZipPath
    {
        get => _zipPath;
        set
        {
            if (SetProperty(ref _zipPath, value))
            {
                OnPropertyChanged(nameof(CanImport));
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? RequestedName
    {
        get => _requestedName;
        set => SetProperty(ref _requestedName, value);
    }

    public string CurrentStep
    {
        get => _currentStep;
        private set => SetProperty(ref _currentStep, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, ClampPercent(value));
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, ClampPercent(value));
    }

    public string? CurrentItem
    {
        get => _currentItem;
        private set => SetProperty(ref _currentItem, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanImport));
                ImportCommand.NotifyCanExecuteChanged();
                CancelImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set => SetProperty(ref _isComplete, value);
    }

    public InstanceImportResult? ImportResult
    {
        get => _importResult;
        private set => SetProperty(ref _importResult, value);
    }

    public AddonUnavailableException? UnavailableAddon
    {
        get => _unavailableAddon;
        private set
        {
            if (SetProperty(ref _unavailableAddon, value))
            {
                OnPropertyChanged(nameof(HasUnavailableAddon));
                OnPropertyChanged(nameof(UnavailableAddonDetails));
            }
        }
    }

    public ImportMode Mode
    {
        get => _importMode;
        set
        {
            if (SetProperty(ref _importMode, value))
            {
                OnPropertyChanged(nameof(IsPackageImport));
                OnPropertyChanged(nameof(IsFolderImport));
                OnPropertyChanged(nameof(CanImport));
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsPackageImport
    {
        get => Mode == ImportMode.Package;
        set
        {
            if (value)
            {
                Mode = ImportMode.Package;
            }
        }
    }

    public bool IsFolderImport
    {
        get => Mode == ImportMode.Folder;
        set
        {
            if (value)
            {
                Mode = ImportMode.Folder;
            }
        }
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value))
            {
                OnPropertyChanged(nameof(CanImport));
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string SelectedServerType
    {
        get => _selectedServerType;
        set => SetProperty(ref _selectedServerType, value);
    }

    public string MinecraftVersion
    {
        get => _minecraftVersion;
        set
        {
            if (SetProperty(ref _minecraftVersion, value))
            {
                OnPropertyChanged(nameof(CanImport));
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ShouldCopyFiles
    {
        get => _shouldCopyFiles;
        set => SetProperty(ref _shouldCopyFiles, value);
    }

    public bool AcceptEula
    {
        get => _acceptEula;
        set
        {
            if (SetProperty(ref _acceptEula, value))
            {
                OnPropertyChanged(nameof(CanImport));
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanImport => !IsImporting && (
        (IsPackageImport && !string.IsNullOrWhiteSpace(ZipPath)) ||
        (IsFolderImport && !string.IsNullOrWhiteSpace(FolderPath) && !string.IsNullOrWhiteSpace(MinecraftVersion) && AcceptEula)
    );
    public bool HasUnavailableAddon => UnavailableAddon is not null;
    public string? UnavailableAddonDetails => UnavailableAddon is null
        ? null
        : $"Provider: {UnavailableAddon.Provider}\nAdd-on: {UnavailableAddon.AddonName}";

    private async Task ImportAsync()
    {
        if (!CanImport)
        {
            return;
        }

        ResetStateForImport();
        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        IsImporting = true;

        try
        {
            InstanceImportResult result;
            if (IsPackageImport)
            {
                CurrentStep = "Starting import...";
                var progress = new Progress<InstanceTransferProgress>(ApplyProgress);
                result = await _importService.ImportAsync(
                        new InstanceImportRequest
                        {
                            ZipPath = ZipPath.Trim(),
                            RequestedName = string.IsNullOrWhiteSpace(RequestedName) ? null : RequestedName.Trim()
                        },
                        progress,
                        cancellation.Token);
            }
            else
            {
                CurrentStep = "Starting folder import...";
                var progress = new Progress<InstanceTransferProgress>(ApplyProgress);
                string defaultName = Path.GetFileName(FolderPath.Trim());
                string importName = string.IsNullOrWhiteSpace(RequestedName) ? defaultName : RequestedName.Trim();
                result = await _importService.ImportLocalFolderAsync(
                        new LocalFolderImportRequest
                        {
                            SourceFolderPath = FolderPath.Trim(),
                            RequestedName = importName,
                            ServerType = SelectedServerType,
                            MinecraftVersion = MinecraftVersion.Trim(),
                            CopyFiles = ShouldCopyFiles,
                            Description = Description?.Trim()
                        },
                        progress,
                        cancellation.Token);
            }

            ImportResult = result;
            IsComplete = true;
            OverallProgress = 100;
            DownloadProgress = 100;
            CurrentStep = "Import complete.";
            ImportCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            CurrentStep = "Import canceled.";
            DownloadProgress = 0;
        }
        catch (AddonUnavailableException ex)
        {
            UnavailableAddon = ex;
            HasError = true;
            ErrorMessage = $"{ex.Message}{Environment.NewLine}Provider: {ex.Provider}{Environment.NewLine}Add-on: {ex.AddonName}";
            CurrentStep = "Add-on unavailable.";
            AddonUnavailable?.Invoke(this, ex);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            CurrentStep = "Import failed.";
        }
        finally
        {
            _importCancellation = null;
            IsImporting = false;
        }
    }

    private void CancelImport()
    {
        if (!IsImporting)
        {
            return;
        }

        CurrentStep = "Canceling import...";
        _importCancellation?.Cancel();
    }

    private void ResetStateForImport()
    {
        HasError = false;
        ErrorMessage = null;
        UnavailableAddon = null;
        ImportResult = null;
        IsComplete = false;
        OverallProgress = 0;
        DownloadProgress = 0;
        CurrentItem = null;
    }

    private void ApplyProgress(InstanceTransferProgress progress)
    {
        if (IsComplete || HasError || !IsImporting)
        {
            return;
        }

        CurrentStep = string.IsNullOrWhiteSpace(progress.CurrentStep)
            ? CurrentStep
            : progress.CurrentStep;
        OverallProgress = progress.OverallProgress;
        DownloadProgress = progress.DownloadProgress;
        CurrentItem = progress.CurrentItem;
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);
}

public enum ImportMode
{
    Package,
    Folder
}
