using PocketMC.Desktop.Features.Marketplace.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class ResolvedDependency : Core.Mvvm.ViewModelBase
    {
        private bool _isSelected;
        public string ProjectId { get; set; } = "";
        public string ProjectTitle { get; set; } = "";
        public string? VersionId { get; set; }
        public string VersionName { get; set; } = "";
        public DependencyType Type { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? Hash { get; set; }
        public string? HashType { get; set; }
        public string ReleaseType { get; set; } = "release";
        public bool IsAlreadyInstalled { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        public string? Error { get; set; }
        public string? Warning { get; set; }
        public string? IdAlias { get; set; }
        public bool IsCheckboxEnabled { get; set; }
        public string? ClientSide { get; set; }
        public string? ServerSide { get; set; }
        public string? SelectedLoader { get; set; }
        public string? MatchedMinecraftVersion { get; set; }
        public string? IconUrl { get; set; }
        public string Provider { get; set; } = "";
    }

    public class DependencyResolverService
    {
        private readonly AddonManifestService _manifestService;

        public DependencyResolverService(AddonManifestService manifestService)
        {
            _manifestService = manifestService;
        }

        public async Task<List<ResolvedDependency>> ResolveAsync(
            IAddonProvider provider,
            string serverDir,
            string rootProjectId,
            string mcVersion,
            string loader,
            EngineCompatibility compat)
        {
            var results = new List<ResolvedDependency>();
            var visited = new HashSet<string>(); // ProjectId to handle cycles
            return await ResolveRecursiveAsync(provider, serverDir, rootProjectId, null, mcVersion, loader, results, visited, DependencyType.Required, compat, true);
        }

        private static string FirstCharToUpper(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            if (input.Length == 1) return input.ToUpperInvariant();
            return char.ToUpperInvariant(input[0]) + input[1..];
        }

        private static bool IsFileCompatibleWithEngine(string fileName, EngineCompatibility compat)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            var fn = fileName.ToLowerInvariant();

            if (compat.Family == EngineFamily.Spigot)
            {
                if (fn.Contains("fabric") || fn.Contains("forge") || fn.Contains("neoforge") || fn.Contains("quilt"))
                {
                    return false;
                }
            }
            else if (compat.Family == EngineFamily.Fabric)
            {
                if (fn.Contains("forge") || fn.Contains("neoforge") || fn.Contains("bukkit") || fn.Contains("spigot") || fn.Contains("paper"))
                {
                    return false;
                }
            }
            else if (compat.Family == EngineFamily.Forge)
            {
                if (fn.Contains("fabric") || fn.Contains("neoforge") || fn.Contains("quilt") || fn.Contains("bukkit") || fn.Contains("spigot") || fn.Contains("paper"))
                {
                    return false;
                }
            }
            else if (compat.Family == EngineFamily.NeoForge)
            {
                if (fn.Contains("fabric") || fn.Contains("quilt") || fn.Contains("bukkit") || fn.Contains("spigot") || fn.Contains("paper") || (fn.Contains("forge") && !fn.Contains("neoforge")))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<List<ResolvedDependency>> ResolveRecursiveAsync(
            IAddonProvider provider,
            string serverDir,
            string projectId,
            string? versionId,
            string mcVersion,
            string loader,
            List<ResolvedDependency> results,
            HashSet<string> visited,
            DependencyType depType,
            EngineCompatibility compat,
            bool isRoot = false)
        {
            string normalizedId = projectId.ToLowerInvariant().Trim();

            // Phase 1: Check cycle/visited
            var existing = results.FirstOrDefault(r =>
                r.ProjectId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase) ||
                (r.IdAlias != null && r.IdAlias.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                if (depType == DependencyType.Required && existing.Type == DependencyType.Optional)
                {
                    existing.Type = DependencyType.Required;
                    existing.IsSelected = true;
                }
                return results;
            }

            if (visited.Contains(normalizedId)) return results;
            visited.Add(normalizedId);

            bool alreadyInstalled = await _manifestService.IsInstalledAsync(serverDir, provider.Name, projectId, compat);

            MarketplaceVersion? version = null;

            // Try resolving by exact versionId first
            if (!string.IsNullOrEmpty(versionId))
            {
                try
                {
                    var resolvedVer = await provider.GetVersionByIdAsync(versionId);
                    if (resolvedVer != null)
                    {
                        if (IsFileCompatibleWithEngine(resolvedVer.FileName, compat))
                        {
                            version = resolvedVer;
                            version.SelectedLoader = compat.LoaderName;
                            version.MatchedMinecraftVersion = mcVersion;
                        }
                    }
                }
                catch
                {
                    // Fallback to latest compat version below
                }
            }

            // Fallback to latest compatible version
            if (version == null)
            {
                version = await provider.GetLatestVersionAsync(projectId, mcVersion, compat.CompatibleLoaderNames);
            }

            if (version == null)
            {
                var projectInfo = await provider.GetProjectInfoAsync(projectId);
                string title = projectInfo?.Title ?? projectId;
                string? iconUrl = projectInfo?.IconUrl;

                var mcCandidates = ModrinthService.BuildMinecraftVersionCandidates(mcVersion);
                string triedMcVersions = string.Join(" or ", mcCandidates.Where(c => !string.IsNullOrEmpty(c)));
                string triedLoaders = string.Join("/", compat.CompatibleLoaderNames.Select(l => FirstCharToUpper(l)));
                string errorMsg = $"No compatible {triedLoaders} version found for Minecraft {triedMcVersions}.";

                results.Add(new ResolvedDependency
                {
                    ProjectId = projectId,
                    ProjectTitle = title,
                    Type = depType,
                    Error = errorMsg,
                    IsAlreadyInstalled = alreadyInstalled,
                    IsCheckboxEnabled = false,
                    IconUrl = iconUrl,
                    Provider = provider.Name
                });
                return results;
            }

            // Phase 2: Canonical ID Check
            string canonicalId = version.ProjectId.ToLowerInvariant();
            var canonicalExisting = results.FirstOrDefault(r => r.ProjectId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase));

            if (canonicalExisting != null)
            {
                // Map the requested alias to the existing canonical result for future cycle detection
                canonicalExisting.IdAlias = normalizedId;

                if (depType == DependencyType.Required && canonicalExisting.Type == DependencyType.Optional)
                {
                    canonicalExisting.Type = DependencyType.Required;
                    canonicalExisting.IsSelected = true;
                }
                return results;
            }

            if (canonicalId != normalizedId)
            {
                if (visited.Contains(canonicalId)) return results;
                visited.Add(canonicalId);
            }

            bool isCheckboxEnabled = depType switch
            {
                DependencyType.Required => alreadyInstalled, // Enabled if already installed (optional reinstall)
                DependencyType.Optional => true,
                _ => false
            };

            bool isSelected = false;
            if (!alreadyInstalled)
            {
                // If not installed, Required and Optional are selected by default
                isSelected = (depType == DependencyType.Required || depType == DependencyType.Optional);
            }
            else
            {
                // If already installed, only pre-select the root item (the item the user clicked "Reinstall" on)
                isSelected = isRoot;
            }

            var resolved = new ResolvedDependency
            {
                ProjectId = version.ProjectId,
                ProjectTitle = version.ProjectTitle,
                VersionId = version.Id,
                VersionName = version.Name,
                Type = depType,
                DownloadUrl = version.DownloadUrl,
                FileName = version.FileName,
                Hash = version.Hash,
                HashType = version.HashType,
                ReleaseType = version.ReleaseType ?? "release",
                IsAlreadyInstalled = alreadyInstalled,
                IsSelected = isSelected,
                IsCheckboxEnabled = isCheckboxEnabled,
                IdAlias = normalizedId,
                Warning = version.Warnings.FirstOrDefault(),
                ClientSide = version.ClientSide,
                ServerSide = version.ServerSide,
                SelectedLoader = version.SelectedLoader,
                MatchedMinecraftVersion = version.MatchedMinecraftVersion,
                IconUrl = version.IconUrl,
                Provider = provider.Name
            };

            results.Add(resolved);

            if (version.Dependencies != null)
            {
                foreach (var dep in version.Dependencies)
                {
                    if (dep.Type == DependencyType.Incompatible) continue;
                    if (dep.Type == DependencyType.Embedded) continue;

                    await ResolveRecursiveAsync(provider, serverDir, dep.ProjectId, dep.VersionId, mcVersion, loader, results, visited, dep.Type, compat, false);
                }
            }

            return results;
        }
    }
}

