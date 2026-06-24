using System.IO;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Desktop.Features.Instances.Updates;

public sealed class AddonMigrationApplier
{
    private readonly AddonManifestService _manifestService;

    public AddonMigrationApplier(AddonManifestService manifestService)
    {
        _manifestService = manifestService;
    }

    public async Task ApplyAsync(
        AddonMigrationPlan plan,
        CancellationToken cancellationToken = default)
    {
        await ApplyFileSwapsAsync(plan, cancellationToken);
        await UpdateManifestAsync(plan, cancellationToken);
    }

    public async Task ApplyFileSwapsAsync(
        AddonMigrationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string serverRoot = Path.GetFullPath(plan.ServerDir);
        var items = plan.Items
            .Where(item => item.Action is AddonMigrationAction.Update or AddonMigrationAction.AddDependency)
            .ToArray();

        ValidateItems(serverRoot, items);

        foreach (AddonMigrationItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string targetPath = ResolveAddonTargetPath(serverRoot, item.TargetSubDirectory, item.TargetFileName);
            string targetDirectory = Path.GetDirectoryName(targetPath)
                ?? throw new InvalidOperationException($"Cannot resolve addon target directory for '{item.TargetFileName}'.");
            Directory.CreateDirectory(targetDirectory);

            string tempFileName = $".pocketmc-addon-apply-{Guid.NewGuid():N}.tmp";
            string tempPath = Path.Combine(targetDirectory, tempFileName);

            await FileUtils.CopyFileAsync(item.StagedFilePath!, tempPath, overwrite: true);

            try
            {
                if (File.Exists(targetPath))
                {
                    string backupFileName = $".pocketmc-addon-backup-{Guid.NewGuid():N}.tmp";
                    string backupPath = Path.Combine(targetDirectory, backupFileName);
                    try
                    {
                        File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
                    }
                    catch
                    {
                        try { File.Delete(tempPath); } catch { }
                        throw;
                    }

                    try { File.Delete(backupPath); } catch { }
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }
            catch (Exception)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            if (item.Action == AddonMigrationAction.Update &&
                !string.IsNullOrWhiteSpace(item.CurrentFileName) &&
                !string.Equals(item.CurrentFileName, item.TargetFileName, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = ResolveAddonTargetPath(serverRoot, item.TargetSubDirectory, item.CurrentFileName);
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }
        }
    }

    public async Task UpdateManifestAsync(
        AddonMigrationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string serverRoot = Path.GetFullPath(plan.ServerDir);
        var items = plan.Items
            .Where(item => item.Action is AddonMigrationAction.Update or AddonMigrationAction.AddDependency)
            .ToArray();
        AddonManifest manifest = await _manifestService.LoadManifestAsync(serverRoot);
        foreach (AddonMigrationItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = manifest.Entries.FirstOrDefault(entry =>
                entry.Provider.Equals(item.Provider, StringComparison.OrdinalIgnoreCase) &&
                entry.ProjectId.Equals(item.ProjectId, StringComparison.OrdinalIgnoreCase));

            string? projectTitle = !string.IsNullOrEmpty(item.ProjectTitle) ? item.ProjectTitle : existing?.ProjectTitle;
            string? projectSlug = existing?.ProjectSlug;
            string? iconUrl = existing?.IconUrl;
            string? displayName = existing?.DisplayName ?? (!string.IsNullOrEmpty(item.ProjectTitle) ? item.ProjectTitle : null);

            manifest.Entries.RemoveAll(entry =>
                entry.Provider.Equals(item.Provider, StringComparison.OrdinalIgnoreCase) &&
                entry.ProjectId.Equals(item.ProjectId, StringComparison.OrdinalIgnoreCase));

            manifest.Entries.Add(new AddonManifestEntry
            {
                Provider = item.Provider,
                ProjectId = item.ProjectId,
                VersionId = item.TargetVersionId,
                FileName = MarketplaceFileNameSanitizer.RequireSafeFileName(item.TargetFileName),
                InstalledAt = DateTime.UtcNow,
                ProjectTitle = projectTitle,
                ProjectSlug = projectSlug,
                IconUrl = iconUrl,
                DisplayName = displayName
            });
        }

        await _manifestService.SaveManifestAsync(serverRoot, manifest);
    }

    private static void ValidateItems(string serverRoot, IReadOnlyList<AddonMigrationItem> items)
    {
        foreach (AddonMigrationItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StagedFilePath))
            {
                throw new FileNotFoundException($"Addon '{item.ProjectId}' was not staged.");
            }

            var stagedInfo = new FileInfo(item.StagedFilePath);
            if (!stagedInfo.Exists || stagedInfo.Length == 0)
            {
                throw new FileNotFoundException($"Staged addon artifact for '{item.ProjectId}' was not found or is empty.", item.StagedFilePath);
            }

            _ = ResolveAddonTargetPath(serverRoot, item.TargetSubDirectory, item.TargetFileName);
            if (!string.IsNullOrWhiteSpace(item.CurrentFileName))
            {
                _ = MarketplaceFileNameSanitizer.RequireSafeFileName(item.CurrentFileName);
            }
        }
    }

    private static string ResolveAddonTargetPath(string serverRoot, string subDirectory, string fileName)
    {
        string safeFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(fileName);
        string? targetDirectory = PathSafety.ValidateContainedPath(serverRoot, subDirectory);
        if (targetDirectory == null)
        {
            throw new InvalidOperationException($"Addon target directory '{subDirectory}' is invalid.");
        }

        return PathSafety.ValidateContainedPath(targetDirectory, safeFileName)
            ?? throw new InvalidOperationException($"Addon target file '{safeFileName}' is invalid.");
    }
}
