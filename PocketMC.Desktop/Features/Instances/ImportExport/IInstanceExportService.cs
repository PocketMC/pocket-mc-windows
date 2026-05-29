using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public interface IInstanceExportService
{
    Task<InstanceExportResult> ExportAsync(
        InstanceExportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class InstanceExportRequest
{
    public InstanceMetadata Metadata { get; set; } = new();
    public string InstancePath { get; set; } = string.Empty;
    public string DestinationZipPath { get; set; } = string.Empty;
    public bool IncludeWorlds { get; set; } = true;
}

public sealed class InstanceExportResult
{
    public string ZipPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public InstanceExportManifest Manifest { get; set; } = new();
    public IReadOnlyList<string> SkippedFiles { get; set; } = Array.Empty<string>();
}
