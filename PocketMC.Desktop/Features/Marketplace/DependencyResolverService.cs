using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace.Models;

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
        public bool IsAlreadyInstalled { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        public string? Error { get; set; }
        public string? IdAlias { get; set; }
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
            string loader)
        {
            var results = new List<ResolvedDependency>();
            var visited = new HashSet<string>(); // ProjectId to handle cycles
            return await ResolveRecursiveAsync(provider, serverDir, rootProjectId, mcVersion, loader, results, visited, DependencyType.Required);
        }

        private async Task<List<ResolvedDependency>> ResolveRecursiveAsync(
            IAddonProvider provider,
            string serverDir,
            string projectId,
            string mcVersion,
            string loader,
            List<ResolvedDependency> results,
            HashSet<string> visited,
            DependencyType depType)
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

            bool alreadyInstalled = await _manifestService.IsInstalledAsync(serverDir, provider.Name, projectId);
            
            var version = await provider.GetLatestVersionAsync(projectId, mcVersion, loader);
            if (version == null)
            {
                results.Add(new ResolvedDependency
                {
                    ProjectId = projectId,
                    ProjectTitle = projectId, // Fallback
                    Type = depType,
                    IsSelected = (depType == DependencyType.Required),
                    Error = "No compatible version found for this Minecraft version/loader.",
                    IsAlreadyInstalled = alreadyInstalled
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

            var resolved = new ResolvedDependency
            {
                ProjectId = version.ProjectId,
                ProjectTitle = version.ProjectTitle,
                VersionId = version.Id,
                VersionName = version.Name,
                Type = depType,
                DownloadUrl = version.DownloadUrl,
                FileName = version.FileName,
                IsAlreadyInstalled = alreadyInstalled,
                IsSelected = (depType == DependencyType.Required || depType == DependencyType.Optional) && !alreadyInstalled,
                IdAlias = normalizedId
            };

            if (depType == DependencyType.Optional && !alreadyInstalled)
            {
                resolved.IsSelected = true;
            }

            results.Add(resolved);

            if (version.Dependencies != null)
            {
                foreach (var dep in version.Dependencies)
                {
                    if (dep.Type == DependencyType.Incompatible) continue;
                    if (dep.Type == DependencyType.Embedded) continue; 

                    await ResolveRecursiveAsync(provider, serverDir, dep.ProjectId, mcVersion, loader, results, visited, dep.Type);
                }
            }

            return results;
        }
    }
}
