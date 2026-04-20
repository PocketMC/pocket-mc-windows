using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using Xunit;

namespace PocketMC.Desktop.Tests
{
    public class DependencyResolverTests
    {
        private class MockProvider : IAddonProvider
        {
            public string Name => "Mock";
            public Dictionary<string, MarketplaceVersion> Versions = new();

            public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader)
            {
                return Task.FromResult(Versions.GetValueOrDefault(projectId));
            }

            public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId) => Task.FromResult<MarketplaceVersion?>(null);
            public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId) => Task.FromResult<MarketplaceProjectInfo?>(null);
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
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric");

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
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric");

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
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric");

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
            var results = await resolver.ResolveAsync(provider, "dummy_dir", "A", "1.20.1", "fabric");

            // Assert
            Assert.Single(results);
            Assert.Equal("A", results[0].ProjectId);
        }
    }
}
