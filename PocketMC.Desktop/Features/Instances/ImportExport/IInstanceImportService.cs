using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public interface IInstanceImportService
{
    Task<InstanceExportManifest> ReadManifestAsync(
        string zipPath,
        CancellationToken cancellationToken = default);

    Task<InstanceImportStagingResult> StageImportAsync(
        InstanceImportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task ReconstructStagedImportAsync(
        InstanceImportStagingResult stagingResult,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<InstanceImportResult> ImportAsync(
        InstanceImportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class InstanceImportRequest
{
    public string ZipPath { get; set; } = string.Empty;
    public string? RequestedName { get; set; }
    public bool IncludeWorlds { get; set; } = true;
}

public sealed class InstanceImportResult
{
    public Guid InstanceId { get; set; }
    public string InstancePath { get; set; } = string.Empty;
    public InstanceMetadata Metadata { get; set; } = new();
    public InstanceExportManifest Manifest { get; set; } = new();
    public IReadOnlyList<InstanceAddonManifest> SkippedAddons { get; set; } = Array.Empty<InstanceAddonManifest>();
}

public sealed class InstanceImportStagingResult
{
    public Guid OperationId { get; set; }
    public string StagingDirectory { get; set; } = string.Empty;
    public string ServerDirectory { get; set; } = string.Empty;
    public string MetadataPath { get; set; } = string.Empty;
    public InstanceExportManifest Manifest { get; set; } = new();
}

public sealed class InstanceTransferProgress
{
    public string CurrentStep { get; set; } = string.Empty;
    public double OverallProgress { get; set; }
    public double DownloadProgress { get; set; }
    public string? CurrentItem { get; set; }
}
