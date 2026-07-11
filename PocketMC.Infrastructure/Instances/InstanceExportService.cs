using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Domain.Models;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure.Marketplace;

namespace PocketMC.Infrastructure.Instances;

public sealed class InstanceExportService : IInstanceExportService
{
    private readonly ExportManifestBuilder _manifestBuilder;
    private readonly ExportFileEnumerator _fileEnumerator;
    private readonly ExportZipWriter _zipWriter;
    private readonly ILogger<InstanceExportService> _logger;
    private CancellationTokenSource? _activeCts;

    public InstanceExportService(
        ExportManifestBuilder manifestBuilder,
        ExportFileEnumerator fileEnumerator,
        ExportZipWriter zipWriter,
        ILogger<InstanceExportService> logger)
    {
        _manifestBuilder = manifestBuilder;
        _fileEnumerator = fileEnumerator;
        _zipWriter = zipWriter;
        _logger = logger;
    }

    /// <summary>
    /// Legacy constructor for backward compatibility and tests.
    /// </summary>
    internal InstanceExportService(
        AddonManifestService addonManifestService,
        ApplicationState applicationState,
        ILogger<InstanceExportService> logger)
    {
        var addonExportService = new AddonExportService(addonManifestService);
        _manifestBuilder = new ExportManifestBuilder(addonExportService);
        _fileEnumerator = new ExportFileEnumerator();
        _zipWriter = new ExportZipWriter(applicationState);
        _logger = logger;
    }

    public bool IsActive => _activeCts != null;
    public void Cancel() => _activeCts?.Cancel();

    public async Task<InstanceExportResult> ExportAsync(
        InstanceExportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken linkedToken = _activeCts.Token;

        string instanceRoot = ValidateInstanceRoot(request.InstancePath);
        string destinationZipPath = ValidateDestinationPath(request.DestinationZipPath, instanceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationZipPath)!);

        InstanceMetadata metadata = request.Metadata ?? throw new ArgumentException("Export metadata is required.", nameof(request));
        bool isJava = metadata.Compatibility.IsJavaEngine;
        string tempZipPath = Path.Combine(
            Path.GetDirectoryName(destinationZipPath)!,
            $".{Path.GetFileName(destinationZipPath)}.{Guid.NewGuid():N}.tmp");
        var skippedFiles = new List<string>();

        try
        {
            Report(progress, "Preparing export manifest...", 0);
            InstanceExportManifest manifest = await _manifestBuilder.BuildManifestAsync(metadata, instanceRoot, isJava, linkedToken)
                .ConfigureAwait(false);

            long totalBytes = _fileEnumerator.EstimateExportBytes(instanceRoot, isJava, request.IncludeWorlds, skippedFiles, linkedToken);

            var files = _fileEnumerator.EnumerateExportFilesAsync(instanceRoot, isJava, request.IncludeWorlds, skippedFiles, linkedToken);

            await _zipWriter.WriteZipAsync(tempZipPath, manifest, metadata, instanceRoot, isJava, totalBytes, files, progress, linkedToken)
                .ConfigureAwait(false);

            ReplaceDestination(tempZipPath, destinationZipPath);
            var info = new FileInfo(destinationZipPath);
            Report(progress, "Export complete.", 100);

            return new InstanceExportResult
            {
                ZipPath = destinationZipPath,
                SizeBytes = info.Exists ? info.Length : 0,
                Manifest = manifest,
                SkippedFiles = skippedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export instance {InstanceName} to {Path}.", metadata.Name, destinationZipPath);
            TryDeleteFile(tempZipPath);
            throw;
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    private static string ValidateInstanceRoot(string instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            throw new ArgumentException("Instance path is required.", nameof(instancePath));
        }

        string root = Path.GetFullPath(instancePath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Instance path '{root}' does not exist.");
        }

        return root;
    }

    private static string ValidateDestinationPath(string destinationZipPath, string instanceRoot)
    {
        if (string.IsNullOrWhiteSpace(destinationZipPath))
        {
            throw new ArgumentException("Destination ZIP path is required.", nameof(destinationZipPath));
        }

        string destination = Path.GetFullPath(destinationZipPath);
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Instance exports must use a .zip file extension.");
        }

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidDataException("Destination ZIP path must include a directory.");
        }

        string containedProbe = Path.GetRelativePath(instanceRoot, destination);
        if (!containedProbe.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(containedProbe))
        {
            throw new InvalidOperationException("Export ZIP cannot be written inside the instance directory.");
        }

        return destination;
    }

    private static void ReplaceDestination(string tempZipPath, string destinationZipPath)
    {
        if (File.Exists(destinationZipPath))
        {
            string backupPath = $"{destinationZipPath}.{Guid.NewGuid():N}.bak";
            File.Replace(tempZipPath, destinationZipPath, backupPath, ignoreMetadataErrors: true);
            TryDeleteFile(backupPath);
            return;
        }

        File.Move(tempZipPath, destinationZipPath);
    }

    private static void Report(
        IProgress<InstanceTransferProgress>? progress,
        string step,
        double overallProgress,
        string? currentItem = null)
    {
        progress?.Report(new InstanceTransferProgress
        {
            CurrentStep = step,
            OverallProgress = overallProgress,
            CurrentItem = currentItem
        });
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
