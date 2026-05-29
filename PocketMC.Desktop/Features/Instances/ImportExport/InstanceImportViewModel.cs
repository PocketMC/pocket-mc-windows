using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

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

    public bool CanImport => !IsImporting && !string.IsNullOrWhiteSpace(ZipPath);
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
            CurrentStep = "Starting import...";
            var progress = new Progress<InstanceTransferProgress>(ApplyProgress);
            InstanceImportResult result = await _importService.ImportAsync(
                    new InstanceImportRequest
                    {
                        ZipPath = ZipPath.Trim(),
                        RequestedName = string.IsNullOrWhiteSpace(RequestedName) ? null : RequestedName.Trim()
                    },
                    progress,
                    cancellation.Token);

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
