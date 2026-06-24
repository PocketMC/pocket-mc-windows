using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Desktop.Features.Instances.Updates;

public sealed class AddonMigrationStager
{
    private readonly DownloaderService _downloader;

    public AddonMigrationStager(DownloaderService downloader)
    {
        _downloader = downloader;
    }

    public async Task StageAsync(
        AddonMigrationPlan plan,
        string addonStagingDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string stagingRoot = Path.GetFullPath(addonStagingDirectory);
        Directory.CreateDirectory(stagingRoot);

        foreach (AddonMigrationItem item in plan.Items.Where(item =>
                     item.Action is AddonMigrationAction.Update or AddonMigrationAction.AddDependency))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(item.DownloadUrl))
            {
                throw new InvalidOperationException($"Addon '{item.ProjectId}' does not include a download URL.");
            }

            string safeFileName = MarketplaceDownloadPolicy.RequireCompatibleFileName(item.TargetFileName, plan.TargetCompatibility);
            string? stagedPath = PathSafety.ValidateContainedPath(stagingRoot, safeFileName)
                ?? throw new InvalidOperationException($"Invalid staged addon file path for '{safeFileName}'.");

            await _downloader.DownloadFileAsync(
                item.DownloadUrl,
                stagedPath,
                item.Hash,
                item.HashType,
                progress,
                cancellationToken);

            ValidateNonEmptyFile(stagedPath);
            item.StagedFilePath = stagedPath;
        }
    }

    private static void ValidateNonEmptyFile(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length == 0)
        {
            throw new IOException($"Staged addon artifact '{filePath}' is missing or empty.");
        }
    }
}
