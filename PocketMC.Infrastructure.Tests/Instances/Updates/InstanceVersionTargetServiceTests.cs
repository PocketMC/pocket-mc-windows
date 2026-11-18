using PocketMC.Application.Interfaces.Instances;
using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Tests.Instances.Updates;

public sealed class InstanceVersionTargetServiceTests
{
    [Fact]
    public async Task GetAvailableTargetVersionsAsync_ReturnsAllReleaseVersionsNewestFirst()
    {
        var provider = new FakeServerSoftwareProvider(
            "Paper (High Performance)",
            "1.21.1",
            "1.20.4",
            "1.21.4",
            "1.20.1");
        var service = new InstanceVersionTargetService(new[] { provider });

        IReadOnlyList<MinecraftVersion> versions = await service.GetAvailableTargetVersionsAsync(
            new InstanceMetadata { ServerType = "Paper", MinecraftVersion = "1.20.4" });

        Assert.Equal(new[] { "1.21.4", "1.21.1", "1.20.4", "1.20.1" }, versions.Select(version => version.Id));
    }

    [Fact]
    public async Task GetAvailableTargetVersionsAsync_KeepsCurrentVersionSelectable()
    {
        var provider = new FakeServerSoftwareProvider("Fabric", "1.21.4", "1.21.1", "1.20.4");
        var service = new InstanceVersionTargetService(new[] { provider });

        IReadOnlyList<MinecraftVersion> versions = await service.GetAvailableTargetVersionsAsync(
            new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.21.1" });

        Assert.Contains(versions, version => version.Id == "1.21.1");
        Assert.Equal("1.21.4", versions[0].Id);
    }

    [Fact]
    public async Task GetAvailableLoaderVersionsAsync_ReturnsBuildsForSelectedMinecraftVersion()
    {
        var provider = new FakeServerSoftwareProvider("NeoForge", "1.21.1", "1.20.4");
        provider.SetBuilds("1.21.1", "21.1.265", "21.1.200", "21.1.190");
        provider.SetBuilds("1.20.4", "20.4.237");
        var service = new InstanceVersionTargetService(new[] { provider });

        IReadOnlyList<ModLoaderVersion> builds = await service.GetAvailableLoaderVersionsAsync(
            new InstanceMetadata { ServerType = "NeoForge", MinecraftVersion = "1.21.1", LoaderVersion = "21.1.190" },
            "1.21.1");

        Assert.Equal(new[] { "21.1.265", "21.1.200", "21.1.190" }, builds.Select(build => build.Version));
    }

    [Fact]
    public async Task GetAvailableLoaderVersionsAsync_AllowsOlderMinecraftVersionBuilds()
    {
        var provider = new FakeServerSoftwareProvider("NeoForge", "1.21.1", "1.20.4");
        provider.SetBuilds("1.20.4", "20.4.237");
        var service = new InstanceVersionTargetService(new[] { provider });

        IReadOnlyList<ModLoaderVersion> builds = await service.GetAvailableLoaderVersionsAsync(
            new InstanceMetadata { ServerType = "NeoForge", MinecraftVersion = "1.20.4", LoaderVersion = "21.1.265" });

        Assert.Equal(new[] { "20.4.237" }, builds.Select(build => build.Version));
    }

    private sealed class FakeServerSoftwareProvider : IServerSoftwareProvider
    {
        private readonly List<MinecraftVersion> _versions;
        private readonly Dictionary<string, List<ModLoaderVersion>> _builds = new(StringComparer.OrdinalIgnoreCase);

        public FakeServerSoftwareProvider(string displayName, params string[] versionIds)
        {
            DisplayName = displayName;
            _versions = versionIds
                .Select(id => new MinecraftVersion { Id = id, Type = "release" })
                .ToList();
        }

        public string DisplayName { get; }

        public void SetBuilds(string minecraftVersion, params string[] loaderVersions)
        {
            _builds[minecraftVersion] = loaderVersions
                .Select(version => new ModLoaderVersion { Version = version })
                .ToList();
        }

        public Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            return Task.FromResult(_versions);
        }

        public Task<List<ModLoaderVersion>> GetBuildsAsync(string versionId)
        {
            return Task.FromResult(_builds.TryGetValue(versionId, out List<ModLoaderVersion>? builds)
                ? builds
                : new List<ModLoaderVersion>());
        }

        public Task<string> DownloadSoftwareAsync(
            string versionId,
            string destinationPath,
            string? loaderVersion = null,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
