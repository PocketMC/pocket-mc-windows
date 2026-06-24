using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public sealed class InstanceExportViewModel : ObservableObject
{
    private readonly IInstanceExportService _exportService;
    private readonly InstanceMetadata _metadata;
    private readonly string _instancePath;
    private CancellationTokenSource? _exportCancellation;
    private string _destinationZipPath;
    private string _currentStep = "Choose where to save the export.";
    private double _overallProgress;
    private string? _currentItem;
    private string? _errorMessage;
    private string? _skippedFilesSummary;
    private bool _includeWorlds = true;
    private bool _isExporting;
    private bool _isComplete;
    private bool _hasError;
    private InstanceExportResult? _exportResult;

    public InstanceExportViewModel(
        IInstanceExportService exportService,
        InstanceMetadata metadata,
        string instancePath)
    {
        _exportService = exportService;
        _metadata = metadata;
        _instancePath = instancePath;
        _destinationZipPath = CreateDefaultDestinationPath(metadata.Name);

        ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport);
        CancelExportCommand = new RelayCommand(CancelExport, () => IsExporting);
    }

    public IAsyncRelayCommand ExportCommand { get; }
    public IRelayCommand CancelExportCommand { get; }

    public event EventHandler<InstanceExportResult>? ExportCompleted;

    public string InstanceName => _metadata.Name;
    public string ServerType => _metadata.ServerType;
    public string MinecraftVersion => _metadata.MinecraftVersion;
    public string ServerSummary => $"{ServerType} - {MinecraftVersion}";
    public bool IsJavaServer => _metadata.Compatibility.IsJavaEngine;

    public string DestinationZipPath
    {
        get => _destinationZipPath;
        set
        {
            if (SetProperty(ref _destinationZipPath, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IncludeWorlds
    {
        get => _includeWorlds;
        set => SetProperty(ref _includeWorlds, value);
    }

    public string CurrentStep
    {
        get => _currentStep;
        private set => SetProperty(ref _currentStep, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, Math.Clamp(value, 0, 100));
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

    public string? SkippedFilesSummary
    {
        get => _skippedFilesSummary;
        private set => SetProperty(ref _skippedFilesSummary, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand.NotifyCanExecuteChanged();
                CancelExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set => SetProperty(ref _isComplete, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public InstanceExportResult? ExportResult
    {
        get => _exportResult;
        private set => SetProperty(ref _exportResult, value);
    }

    public bool HasSkippedFiles => ExportResult?.SkippedFiles.Count > 0;
    public bool CanExport => !IsExporting && !string.IsNullOrWhiteSpace(DestinationZipPath);

    public void UseConfigOnlyDefaultForRunningServer()
    {
        IncludeWorlds = false;
        CurrentStep = "Server is running. Export is set to configuration-only by default.";
    }

    private async Task ExportAsync()
    {
        if (!CanExport)
        {
            return;
        }

        ResetStateForExport();
        using var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;
        IsExporting = true;

        try
        {
            CurrentStep = "Preparing export...";
            var progress = new Progress<InstanceTransferProgress>(ApplyProgress);
            InstanceExportResult result = await _exportService.ExportAsync(
                new InstanceExportRequest
                {
                    Metadata = _metadata,
                    InstancePath = _instancePath,
                    DestinationZipPath = DestinationZipPath.Trim(),
                    IncludeWorlds = IncludeWorlds
                },
                progress,
                cancellation.Token);

            ExportResult = result;
            IsComplete = true;
            OverallProgress = 100;
            CurrentStep = "Export complete.";
            SkippedFilesSummary = result.SkippedFiles.Count == 0
                ? "No binaries or unsafe files were exported."
                : $"Skipped {result.SkippedFiles.Count} binary or transient file{(result.SkippedFiles.Count == 1 ? string.Empty : "s")}.";
            OnPropertyChanged(nameof(HasSkippedFiles));
            ExportCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            CurrentStep = "Export canceled.";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            CurrentStep = "Export failed.";
        }
        finally
        {
            _exportCancellation = null;
            IsExporting = false;
        }
    }

    private void CancelExport()
    {
        if (!IsExporting)
        {
            return;
        }

        CurrentStep = "Canceling export...";
        _exportCancellation?.Cancel();
    }

    private void ResetStateForExport()
    {
        HasError = false;
        ErrorMessage = null;
        ExportResult = null;
        IsComplete = false;
        OverallProgress = 0;
        CurrentItem = null;
        SkippedFilesSummary = null;
        OnPropertyChanged(nameof(HasSkippedFiles));
    }

    private void ApplyProgress(InstanceTransferProgress progress)
    {
        if (IsComplete || HasError || !IsExporting)
        {
            return;
        }

        CurrentStep = string.IsNullOrWhiteSpace(progress.CurrentStep)
            ? CurrentStep
            : progress.CurrentStep;
        OverallProgress = progress.OverallProgress;
        CurrentItem = progress.CurrentItem;
    }

    private static string CreateDefaultDestinationPath(string instanceName)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(instanceName) ? "instance" : instanceName);
        return Path.Combine(desktop, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmm}.zip");
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim('-', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? "instance" : sanitized;
    }
}
