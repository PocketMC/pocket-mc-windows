using PocketMC.Desktop.Features.Marketplace.Models;
using System.IO;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Application.Instances.Updates;

public sealed class AddonMigrationPlanner
{
    private readonly AddonManifestService _manifestService;
    private readonly AddonUpdateService _updateService;
    private readonly DependencyResolverService _dependencyResolver;
    private readonly IReadOnlyDictionary<string, IAddonProvider> _providers;

    public AddonMigrationPlanner(
        AddonManifestService manifestService,
        AddonUpdateService updateService,
        DependencyResolverService dependencyResolver,
        IEnumerable<IAddonProvider> providers)
    {
        _manifestService = manifestService;
        _updateService = updateService;
        _dependencyResolver = dependencyResolver;
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AddonMigrationPlan> BuildPlanAsync(
        string serverDir,
        InstanceMetadata currentMetadata,
        string targetMinecraftVersion,
        EngineCompatibility targetCompatibility,
        InstanceUpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentMetadata);
        string instanceRoot = Path.GetFullPath(serverDir);
        if (!Directory.Exists(instanceRoot))
        {
            throw new DirectoryNotFoundException($"Instance directory not found: {serverDir}");
        }

        var manifest = await _manifestService.LoadManifestAsync(instanceRoot);
        var files = EnumerateAddonFiles(instanceRoot, currentMetadata.Compatibility, targetCompatibility);
        var filesByName = files
            .GroupBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var trackedFileNames = new HashSet<string>(manifest.Entries.Select(entry => entry.FileName), StringComparer.OrdinalIgnoreCase);

        var plan = new AddonMigrationPlan
        {
            ServerDir = instanceRoot,
            TargetMinecraftVersion = targetMinecraftVersion,
            TargetLoaderName = targetCompatibility.LoaderName,
            TargetCompatibility = targetCompatibility,
            UpdateMode = updateMode,
            TrackedAddonCount = manifest.Entries.Count,
            ManualUntrackedAddonCount = files.Count(file => !trackedFileNames.Contains(file.FileName))
        };

        AddManualAddonWarnings(plan, files, trackedFileNames);
        AddBedrockPreservationWarning(plan, currentMetadata.Compatibility, files);

        if (updateMode == InstanceUpdateMode.ServerOnlyWarnAboutAddons)
        {
            if (manifest.Entries.Count > 0 || plan.ManualUntrackedAddonCount > 0)
            {
                plan.Warnings.Add(new AddonMigrationWarning
                {
                    Code = AddonMigrationWarningCode.ServerOnlyAddonUpdatesSkipped,
                    Message = "Addon updates were skipped because server-only update mode is selected."
                });
            }

            return plan;
        }

        if (targetCompatibility.Family == EngineFamily.Bedrock)
        {
            return plan;
        }

        foreach (AddonManifestEntry entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_providers.ContainsKey(entry.Provider))
            {
                plan.Warnings.Add(new AddonMigrationWarning
                {
                    Code = AddonMigrationWarningCode.UnknownProvider,
                    ProjectId = entry.ProjectId,
                    FileName = entry.FileName,
                    Message = $"Provider '{entry.Provider}' is not available, so '{entry.FileName}' cannot be updated automatically."
                });
                plan.IncompatibleAddonCount++;
                continue;
            }

            AddonUpdateCheckResult update = await _updateService.CheckForUpdateFromEntryAsync(
                entry,
                targetMinecraftVersion,
                targetCompatibility.LoaderName,
                targetCompatibility);

            if (!string.IsNullOrWhiteSpace(update.Error))
            {
                plan.Warnings.Add(new AddonMigrationWarning
                {
                    Code = AddonMigrationWarningCode.NoCompatibleUpdate,
                    ProjectId = entry.ProjectId,
                    FileName = entry.FileName,
                    Message = $"No compatible marketplace update was found for '{entry.FileName}' on Minecraft {targetMinecraftVersion}: {update.Error}"
                });
                plan.IncompatibleAddonCount++;
                continue;
            }

            bool shouldStage = update.IsUpdateAvailable || updateMode == InstanceUpdateMode.ExperimentalAggressiveUpdate;
            if (!shouldStage)
            {
                continue;
            }

            if (updateMode == InstanceUpdateMode.ExperimentalAggressiveUpdate && !update.IsUpdateAvailable)
            {
                plan.Warnings.Add(new AddonMigrationWarning
                {
                    Code = AddonMigrationWarningCode.AggressiveUpdate,
                    ProjectId = entry.ProjectId,
                    FileName = entry.FileName,
                    Message = $"Experimental mode will re-stage '{entry.FileName}' even though the provider reports it is current."
                });
            }

            string safeTargetFileName = MarketplaceDownloadPolicy.RequireCompatibleFileName(
                update.LatestFileName ?? entry.FileName,
                targetCompatibility);
            string subDirectory = filesByName.TryGetValue(entry.FileName, out AddonFileInfo? installedFile)
                ? installedFile.SubDirectory
                : ResolveDefaultAddonSubDirectory(targetCompatibility, safeTargetFileName);

            plan.Items.Add(new AddonMigrationItem
            {
                Action = AddonMigrationAction.Update,
                Provider = entry.Provider,
                ProjectId = entry.ProjectId,
                ProjectTitle = update.ProjectTitle ?? entry.ProjectId,
                CurrentVersionId = entry.VersionId,
                TargetVersionId = update.LatestVersionId ?? entry.VersionId,
                CurrentFileName = entry.FileName,
                TargetFileName = safeTargetFileName,
                TargetSubDirectory = subDirectory,
                DownloadUrl = update.LatestDownloadUrl ?? string.Empty,
                Hash = update.Hash,
                HashType = update.HashType,
                VersionName = update.LatestVersionName ?? update.LatestVersionId ?? string.Empty
            });
        }

        plan.CompatibleUpdateCount = plan.Items.Count(item => item.Action == AddonMigrationAction.Update);

        if (updateMode is InstanceUpdateMode.ServerAndCompatibleMarketplaceAddonsAndDependencies
            or InstanceUpdateMode.ExperimentalAggressiveUpdate)
        {
            await AddDependencyItemsAsync(plan, manifest, targetMinecraftVersion, targetCompatibility, cancellationToken);
        }

        return plan;
    }

    private async Task AddDependencyItemsAsync(
        AddonMigrationPlan plan,
        AddonManifest manifest,
        string targetMinecraftVersion,
        EngineCompatibility targetCompatibility,
        CancellationToken cancellationToken)
    {
        var knownProjects = new HashSet<string>(
            manifest.Entries.Select(entry => entry.ProjectId)
                .Concat(plan.Items.Select(item => item.ProjectId)),
            StringComparer.OrdinalIgnoreCase);

        foreach (AddonMigrationItem rootItem in plan.Items.Where(item => item.Action == AddonMigrationAction.Update).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_providers.TryGetValue(rootItem.Provider, out IAddonProvider? provider))
            {
                continue;
            }

            List<ResolvedDependency> dependencies;
            try
            {
                dependencies = await _dependencyResolver.ResolveAsync(
                    provider,
                    plan.ServerDir,
                    rootItem.ProjectId,
                    targetMinecraftVersion,
                    targetCompatibility.LoaderName,
                    targetCompatibility);
            }
            catch (Exception ex)
            {
                plan.Warnings.Add(new AddonMigrationWarning
                {
                    Code = AddonMigrationWarningCode.DependencyResolutionFailed,
                    ProjectId = rootItem.ProjectId,
                    Message = $"Dependencies for '{rootItem.ProjectId}' could not be resolved: {ex.Message}"
                });
                continue;
            }

            foreach (ResolvedDependency dependency in dependencies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (dependency.Error != null)
                {
                    plan.Warnings.Add(new AddonMigrationWarning
                    {
                        Code = AddonMigrationWarningCode.DependencyResolutionFailed,
                        ProjectId = dependency.ProjectId,
                        Message = $"Dependency '{dependency.ProjectId}' is not compatible with Minecraft {targetMinecraftVersion}: {dependency.Error}"
                    });
                    continue;
                }

                if (dependency.IsAlreadyInstalled ||
                    string.IsNullOrWhiteSpace(dependency.DownloadUrl) ||
                    string.IsNullOrWhiteSpace(dependency.FileName) ||
                    !knownProjects.Add(dependency.ProjectId))
                {
                    continue;
                }

                string safeFileName = MarketplaceDownloadPolicy.RequireCompatibleFileName(dependency.FileName, targetCompatibility);
                plan.Items.Add(new AddonMigrationItem
                {
                    Action = AddonMigrationAction.AddDependency,
                    Provider = provider.Name,
                    ProjectId = dependency.ProjectId,
                    ProjectTitle = dependency.ProjectTitle,
                    TargetVersionId = dependency.VersionId ?? string.Empty,
                    TargetFileName = safeFileName,
                    TargetSubDirectory = ResolveDefaultAddonSubDirectory(targetCompatibility, safeFileName),
                    DownloadUrl = dependency.DownloadUrl,
                    Hash = dependency.Hash,
                    HashType = dependency.HashType,
                    VersionName = dependency.VersionName,
                    IsDependency = true
                });
            }
        }

        plan.DependencyAdditionCount = plan.Items.Count(item => item.Action == AddonMigrationAction.AddDependency);
    }

    private static void AddManualAddonWarnings(
        AddonMigrationPlan plan,
        IReadOnlyList<AddonFileInfo> files,
        HashSet<string> trackedFileNames)
    {
        foreach (AddonFileInfo file in files.Where(file => !trackedFileNames.Contains(file.FileName)))
        {
            plan.Warnings.Add(new AddonMigrationWarning
            {
                Code = AddonMigrationWarningCode.ManualAddonNotUpdated,
                FileName = file.FileName,
                Message = $"Manual or untracked addon '{file.FileName}' will be preserved but not updated automatically."
            });
        }
    }

    private static void AddBedrockPreservationWarning(
        AddonMigrationPlan plan,
        EngineCompatibility currentCompatibility,
        IReadOnlyList<AddonFileInfo> files)
    {
        if (currentCompatibility.Family != EngineFamily.Bedrock || files.Count == 0)
        {
            return;
        }

        plan.Warnings.Add(new AddonMigrationWarning
        {
            Code = AddonMigrationWarningCode.BedrockAutomaticUpdateUnsupported,
            Message = "Existing behavior_packs and resource_packs will be preserved. Automatic Bedrock add-on updates are not supported yet without explicit manifest tracking."
        });
    }

    private static List<AddonFileInfo> EnumerateAddonFiles(
        string serverDir,
        EngineCompatibility currentCompatibility,
        EngineCompatibility targetCompatibility)
    {
        var files = new List<AddonFileInfo>();
        var directories = new List<(string SubDirectory, string[] Patterns, bool DirectoryEntries)>
        {
            ("plugins", currentCompatibility.Family == EngineFamily.Pocketmine || targetCompatibility.Family == EngineFamily.Pocketmine
                ? new[] { "*.phar" }
                : new[] { "*.jar" }, false),
            ("mods", new[] { "*.jar" }, false),
            ("behavior_packs", new[] { "*" }, true),
            ("resource_packs", new[] { "*" }, true)
        };

        foreach ((string subDirectory, string[] patterns, bool directoryEntries) in directories)
        {
            string? directory = PathSafety.ValidateContainedPath(serverDir, subDirectory);
            if (directory == null || !Directory.Exists(directory))
            {
                continue;
            }

            if (directoryEntries)
            {
                foreach (string path in Directory.EnumerateDirectories(directory))
                {
                    files.Add(new AddonFileInfo(Path.GetFileName(path), subDirectory, path));
                }
                continue;
            }

            foreach (string pattern in patterns)
            {
                foreach (string path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    files.Add(new AddonFileInfo(Path.GetFileName(path), subDirectory, path));
                }
            }
        }

        return files;
    }

    private static string ResolveDefaultAddonSubDirectory(EngineCompatibility compatibility, string fileName)
    {
        if (fileName.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
        {
            return "plugins";
        }

        if (fileName.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".mcaddon", StringComparison.OrdinalIgnoreCase))
        {
            return "behavior_packs";
        }

        return compatibility.PrimaryAddonSubDir;
    }

    private sealed record AddonFileInfo(string FileName, string SubDirectory, string FullPath);
}

