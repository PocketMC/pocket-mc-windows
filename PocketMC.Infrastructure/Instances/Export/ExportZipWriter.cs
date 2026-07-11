using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Interfaces.Instances;

namespace PocketMC.Infrastructure.Instances;

public class ExportZipWriter
{
    private const int StreamBufferSize = 1024 * 128;
    private const string ExportedMetadataFileName = "pocket-mc.json";
    private const string SourceMetadataFileName = ".pocket-mc.json";

    private readonly ApplicationState _applicationState;

    public ExportZipWriter(ApplicationState applicationState)
    {
        _applicationState = applicationState;
    }

    public async Task WriteZipAsync(
        string tempZipPath,
        InstanceExportManifest manifest,
        InstanceMetadata metadata,
        string instanceRoot,
        bool isJava,
        long totalBytes,
        IAsyncEnumerable<ExportFile> files,
        IProgress<InstanceTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        long copiedBytes = 0;

        await using (var zipStream = new FileStream(tempZipPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = StreamBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        }))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            await AddJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken).ConfigureAwait(false);
            await AddMetadataEntryAsync(archive, metadata, instanceRoot, _applicationState, cancellationToken).ConfigureAwait(false);
            await AddIconEntryAsync(archive, instanceRoot, cancellationToken).ConfigureAwait(false);

            await foreach (ExportFile file in files.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                string entryName = ToZipEntryName(Path.Combine("server", file.RelativePath));
                Report(progress, $"Exporting {file.RelativePath}...", CalculateProgress(copiedBytes, totalBytes), file.RelativePath);

                await AddFileEntryAsync(archive, file.FullPath, entryName, cancellationToken).ConfigureAwait(false);
                copiedBytes += file.SizeBytes;
            }
        }
    }

    private static async Task AddJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(ToZipEntryName(entryName), CompressionLevel.Fastest);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            InstanceExportManifest.CreateJsonOptions(),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddMetadataEntryAsync(
        ZipArchive archive,
        InstanceMetadata metadata,
        string instanceRoot,
        ApplicationState applicationState,
        CancellationToken cancellationToken)
    {
        InstanceMetadata portableMetadata = await CreatePortableMetadataSnapshotAsync(metadata, instanceRoot, applicationState, cancellationToken)
            .ConfigureAwait(false);
        await AddJsonEntryAsync(archive, ExportedMetadataFileName, portableMetadata, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InstanceMetadata> CreatePortableMetadataSnapshotAsync(
        InstanceMetadata requestMetadata,
        string instanceRoot,
        ApplicationState applicationState,
        CancellationToken cancellationToken)
    {
        InstanceMetadata source = requestMetadata;
        string metadataPath = Path.Combine(instanceRoot, SourceMetadataFileName);

        if (File.Exists(metadataPath))
        {
            try
            {
                await using var stream = new FileStream(metadataPath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite,
                    BufferSize = StreamBufferSize,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

                source = await JsonSerializer.DeserializeAsync<InstanceMetadata>(
                        stream,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                    ?? requestMetadata;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                source = requestMetadata;
            }
        }

        InstanceMetadata snapshot = CloneMetadata(source);
        snapshot.Id = Guid.Empty;
        snapshot.LastPlayedAt = null;
        snapshot.LastBackupTime = null;

        string appRoot = string.Empty;
        if (applicationState.IsConfigured)
        {
            appRoot = applicationState.GetRequiredAppRootPath();
        }

        snapshot.CustomJavaPath = MakePathPortable(snapshot.CustomJavaPath, appRoot);
        snapshot.SimpleVoiceChatConfigPath = MakePathPortable(snapshot.SimpleVoiceChatConfigPath, appRoot);
        snapshot.CustomBackupDirectory = null;

        return snapshot;
    }

    private static string? MakePathPortable(string? path, string appRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(appRoot))
        {
            return path;
        }

        try
        {
            if (!Path.IsPathRooted(path))
            {
                return path;
            }

            string fullPath = Path.GetFullPath(path);
            string fullAppRoot = Path.GetFullPath(appRoot);

            if (fullPath.StartsWith(fullAppRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(fullAppRoot, fullPath);
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                {
                    return relative;
                }
            }
        }
        catch
        {
            // Ignore path processing errors, return original.
        }

        return path;
    }

    private static InstanceMetadata CloneMetadata(InstanceMetadata metadata)
    {
        string json = JsonSerializer.Serialize(metadata);
        return JsonSerializer.Deserialize<InstanceMetadata>(json) ?? new InstanceMetadata();
    }

    private static async Task AddIconEntryAsync(ZipArchive archive, string instanceRoot, CancellationToken cancellationToken)
    {
        string iconPath = Path.Combine(instanceRoot, "server-icon.png");
        if (File.Exists(iconPath))
        {
            await AddFileEntryAsync(archive, iconPath, "meta/icon.png", cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AddFileEntryAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(ToZipEntryName(entryName), CompressionLevel.Fastest);

        await using Stream entryStream = entry.Open();
        await using var sourceStream = new FileStream(sourcePath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = StreamBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        await sourceStream.CopyToAsync(entryStream, StreamBufferSize, cancellationToken).ConfigureAwait(false);
    }

    private static string ToZipEntryName(string path) => path.Replace('\\', '/').TrimStart('/');

    private static double CalculateProgress(long copiedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        return Math.Clamp(copiedBytes * 100d / totalBytes, 0, 99);
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
}
