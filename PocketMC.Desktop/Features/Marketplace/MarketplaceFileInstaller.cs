using System.IO;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Application.Instances.Services;

namespace PocketMC.Desktop.Features.Marketplace;

public sealed class MarketplaceFileInstaller
{
    private readonly DownloaderService _downloader;
    private readonly ILogger<MarketplaceFileInstaller> _logger;

    public MarketplaceFileInstaller(DownloaderService downloader, ILogger<MarketplaceFileInstaller> logger)
    {
        _downloader = downloader;
        _logger = logger;
    }

    public async Task InstallAsync(
        string url,
        string destinationPath,
        string? expectedHash,
        string? expectedHashType,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Marketplace destination path must include a directory.");
        }

        Directory.CreateDirectory(directory);

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            _logger.LogWarning("Installing marketplace file {DestinationPath} without provider hash verification.", destinationPath);
        }

        string stagingDir = Path.Combine(directory, ".pocketmc-marketplace-staging");
        Directory.CreateDirectory(stagingDir);
        string stagingPath = Path.Combine(stagingDir, $"{Guid.NewGuid():N}-{Path.GetFileName(destinationPath)}");

        try
        {
            await _downloader.DownloadFileAsync(url, stagingPath, expectedHash, expectedHashType, progress, cancellationToken);
            ValidateDownloadedFile(stagingPath);
            await PromoteAsync(stagingPath, destinationPath, cancellationToken);
        }
        finally
        {
            TryDeleteFile(stagingPath);
            TryDeleteFile(stagingPath + ".partial");
            TryDeleteDirectory(stagingDir);
        }
    }

    private static void ValidateDownloadedFile(string stagingPath)
    {
        var fileInfo = new FileInfo(stagingPath);
        if (!fileInfo.Exists || fileInfo.Length <= 0)
        {
            throw new InvalidDataException("Marketplace download completed without a non-empty staged file.");
        }
    }

    private static async Task PromoteAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(destinationPath))
                {
                    string backupPath = destinationPath + $".replace-{Guid.NewGuid():N}.bak";
                    File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);
                    TryDeleteFile(backupPath);
                }
                else
                {
                    File.Move(sourcePath, destinationPath);
                }

                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        if (File.Exists(destinationPath))
        {
            string backupPath = destinationPath + $".replace-{Guid.NewGuid():N}.bak";
            File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);
            TryDeleteFile(backupPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }
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
            // Best effort cleanup only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}

