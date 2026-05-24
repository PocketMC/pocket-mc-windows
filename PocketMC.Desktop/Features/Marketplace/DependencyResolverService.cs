using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Models;

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
            return await ResolveRecursiveAsync(provider, serverDir, rootProjectId, mcVersion, loader, results, visited, DependencyType.Required, compat, true);
        }

        private async Task<List<ResolvedDependency>> ResolveRecursiveAsync(
            IAddonProvider provider,
            string serverDir,
            string projectId,
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
            
            var version = await provider.GetLatestVersionAsync(projectId, mcVersion, loader);
            if (version == null)
            {
                results.Add(new ResolvedDependency
                {
                    ProjectId = projectId,
                    ProjectTitle = projectId, // Fallback
                    Type = depType,
                    Error = "No compatible version found for this Minecraft version/loader.",
                    IsAlreadyInstalled = alreadyInstalled,
                    IsCheckboxEnabled = false
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
                ReleaseType = version.ReleaseType,
                IsAlreadyInstalled = alreadyInstalled,
                IsSelected = isSelected,
                IsCheckboxEnabled = isCheckboxEnabled,
                IdAlias = normalizedId,
                Warning = version.Warnings.FirstOrDefault()
            };

            results.Add(resolved);

            if (version.Dependencies != null)
            {
                foreach (var dep in version.Dependencies)
                {
                    if (dep.Type == DependencyType.Incompatible) continue;
                    if (dep.Type == DependencyType.Embedded) continue; 

                    await ResolveRecursiveAsync(provider, serverDir, dep.ProjectId, mcVersion, loader, results, visited, dep.Type, compat, false);
                }
            }

            return results;
        }
    }
}
