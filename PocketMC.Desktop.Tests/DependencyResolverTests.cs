using PocketMC.Domain.Models;
using PocketMC.Domain.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests
{
    public class DependencyResolverTests
    {
        private class MockProvider : IAddonProvider
        {
            public string Name => "Mock";
            public Dictionary<string, MarketplaceVersion> Versions = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, MarketplaceVersion> VersionsById = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, MarketplaceProjectInfo> ProjectInfos = new(StringComparer.OrdinalIgnoreCase);

            public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader)
            {
                return Task.FromResult(Versions.GetValueOrDefault(projectId));
            }

            public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates)
            {
                return Task.FromResult(Versions.GetValueOrDefault(projectId));
            }

            public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId)
            {
                if (VersionsById.TryGetValue(versionId, out var v)) return Task.FromResult<MarketplaceVersion?>(v);
                return Task.FromResult<MarketplaceVersion?>(null);
            }

            public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId)
            {
                if (ProjectInfos.TryGetValue(projectId, out var p)) return Task.FromResult<MarketplaceProjectInfo?>(p);
                return Task.FromResult<MarketplaceProjectInfo?>(null);
            }
        }

        [Fact]
        public async Task ResolveAsync_ShouldResolveTransitiveDependencies()
        {
            // Arrange
            var provider = new MockProvider();
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                ProjectTitle = "Mod A",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "B", Type = DependencyType.Required }
                }
            };
            provider.Versions["B"] = new MarketplaceVersion
            {
                ProjectId = "B",
                ProjectTitle = "Mod B",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "C", Type = DependencyType.Optional }
                }
            };
            provider.Versions["C"] = new MarketplaceVersion
            {
                ProjectId = "C",
                ProjectTitle = "Mod C"
            };

            var manifestService = new AddonManifestService(); // Use real service, it just checks files
            var resolver = new DependencyResolverService(manifestService);

            // Act
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            // Assert
            Assert.Equal(3, results.Count);
            Assert.Contains(results, r => r.ProjectId == "A" && r.Type == DependencyType.Required);
            Assert.Contains(results, r => r.ProjectId == "B" && r.Type == DependencyType.Required);
            Assert.Contains(results, r => r.ProjectId == "C" && r.Type == DependencyType.Optional);
            Assert.All(results, r => Assert.True(r.IsSelected)); // Optional should be selected by default
        }

        [Fact]
        public async Task ResolveAsync_ShouldHandleCycles()
        {
            // Arrange
            var provider = new MockProvider();
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "B", Type = DependencyType.Required }
                }
            };
            provider.Versions["B"] = new MarketplaceVersion
            {
                ProjectId = "B",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "A", Type = DependencyType.Required }
                }
            };

            var resolver = new DependencyResolverService(new AddonManifestService());

            // Act
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.ProjectId == "A");
            Assert.Contains(results, r => r.ProjectId == "B");
        }
        [Fact]
        public async Task ResolveAsync_ShouldNormalizeIdsAndHandleDuplicates()
        {
            // Arrange
            var provider = new MockProvider();
            // A depends on "b-slug" (slug)
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "b-slug", Type = DependencyType.Required }
                }
            };
            // "b-slug" actually has ProjectId "B-CANONICAL-ID"
            provider.Versions["b-slug"] = new MarketplaceVersion
            {
                ProjectId = "B-CANONICAL-ID",
                ProjectTitle = "Mod B"
            };
            // Also add the canonical one in case it's looked up directly
            provider.Versions["B-CANONICAL-ID"] = provider.Versions["b-slug"];

            var resolver = new DependencyResolverService(new AddonManifestService());

            // Act
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.ProjectId == "A");
            Assert.Contains(results, r => r.ProjectId == "B-CANONICAL-ID");
            Assert.DoesNotContain(results, r => r.ProjectId == "b-slug");
        }
        [Fact]
        public async Task ResolveAsync_ShouldWorkWhenSlugMatchesId()
        {
            // Arrange
            var provider = new MockProvider();
            // Slug is "A", ProjectId is also "A"
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                ProjectTitle = "Mod A",
                Dependencies = new List<MarketplaceDependency>()
            };

            var resolver = new DependencyResolverService(new AddonManifestService());

            // Act
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            // Assert
            Assert.Single(results);
            Assert.Equal("A", results[0].ProjectId);
        }

        [Fact]
        public async Task ResolveAsync_ShouldResolveDependencyVersionIdViaGetVersionByIdAsync()
        {
            var provider = new MockProvider();
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                ProjectTitle = "Mod A",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "B", VersionId = "B-VER-123", Type = DependencyType.Required }
                }
            };
            provider.VersionsById["B-VER-123"] = new MarketplaceVersion
            {
                Id = "B-VER-123",
                ProjectId = "B",
                ProjectTitle = "Mod B Special File",
                FileName = "special-b.jar"
            };

            var resolver = new DependencyResolverService(new AddonManifestService());

            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            Assert.Equal(2, results.Count);
            var bDep = results.FirstOrDefault(r => r.ProjectId == "B");
            Assert.NotNull(bDep);
            Assert.Equal("B-VER-123", bDep.VersionId);
            Assert.Equal("Mod B Special File", bDep.ProjectTitle);
            Assert.Equal("special-b.jar", bDep.FileName);
        }

        [Fact]
        public async Task ResolveAsync_ShouldFallbackToLatestCompatibleIfVersionIdFetchFails()
        {
            var provider = new MockProvider();
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                ProjectTitle = "Mod A",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "B", VersionId = "B-VER-NONEXISTENT", Type = DependencyType.Required }
                }
            };
            provider.Versions["B"] = new MarketplaceVersion
            {
                Id = "B-LATEST",
                ProjectId = "B",
                ProjectTitle = "Mod B Latest"
            };

            var resolver = new DependencyResolverService(new AddonManifestService());

            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            Assert.Equal(2, results.Count);
            var bDep = results.FirstOrDefault(r => r.ProjectId == "B");
            Assert.NotNull(bDep);
            Assert.Equal("B-LATEST", bDep.VersionId);
            Assert.Equal("Mod B Latest", bDep.ProjectTitle);
        }

        [Fact]
        public async Task ResolveAsync_FailedDependency_FetchesProjectTitleFromGetProjectInfoAsync()
        {
            var provider = new MockProvider();
            provider.Versions["A"] = new MarketplaceVersion
            {
                ProjectId = "A",
                Dependencies = new List<MarketplaceDependency>
                {
                    new MarketplaceDependency { ProjectId = "B", Type = DependencyType.Required }
                }
            };
            provider.ProjectInfos["B"] = new MarketplaceProjectInfo
            {
                Id = "B",
                Title = "Mod B Readable Name"
            };

            var resolver = new DependencyResolverService(new AddonManifestService());

            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric", new EngineCompatibility("Fabric"));

            Assert.Equal(2, results.Count);
            var bDep = results.FirstOrDefault(r => r.ProjectId == "B");
            Assert.NotNull(bDep);
            Assert.Equal("Mod B Readable Name", bDep.ProjectTitle);
            Assert.NotNull(bDep.Error);
        }

        [Fact]
        public void DependencyConfirmationViewModel_OptionalFailure_DoesNotBlockInstall()
        {
            var deps = new List<ResolvedDependency>
            {
                new ResolvedDependency { ProjectId = "A", IsSelected = true, Type = DependencyType.Required },
                new ResolvedDependency { ProjectId = "B", IsSelected = false, Type = DependencyType.Optional, Error = "Failed to resolve B." }
            };

            var vm = new DependencyConfirmationViewModel(deps);

            Assert.False(vm.HasFailedRequiredDependency);
            Assert.True(vm.CanInstall);
        }

        [Fact]
        public void DependencyConfirmationViewModel_RequiredFailure_BlocksInstall()
        {
            var deps = new List<ResolvedDependency>
            {
                new ResolvedDependency { ProjectId = "A", IsSelected = true, Type = DependencyType.Required },
                new ResolvedDependency { ProjectId = "B", IsSelected = false, Type = DependencyType.Required, Error = "Failed to resolve required dependency B." }
            };

            var vm = new DependencyConfirmationViewModel(deps);

            Assert.True(vm.HasFailedRequiredDependency);
            Assert.False(vm.CanInstall);
        }
    }
}



