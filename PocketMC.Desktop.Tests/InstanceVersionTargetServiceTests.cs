using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceVersionTargetServiceTests
{
    [Fact]
    public async Task GetAvailableTargetVersionsAsync_ReturnsAllVersionsAfterCurrentNewestFirst()
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

        Assert.Equal(new[] { "1.21.4", "1.21.1" }, versions.Select(version => version.Id));
    }

    [Fact]
    public async Task GetAvailableTargetVersionsAsync_ReturnsEmpty_WhenCurrentVersionIsLatest()
    {
        var provider = new FakeServerSoftwareProvider(
            "Fabric",
            "1.21.4",
            "1.21.1",
            "1.20.4");
        var service = new InstanceVersionTargetService(new[] { provider });

        IReadOnlyList<MinecraftVersion> versions = await service.GetAvailableTargetVersionsAsync(
            new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.21.4" });

        Assert.Empty(versions);
    }

    private sealed class FakeServerSoftwareProvider : IServerSoftwareProvider
    {
        private readonly List<MinecraftVersion> _versions;

        public FakeServerSoftwareProvider(string displayName, params string[] versionIds)
        {
            DisplayName = displayName;
            _versions = versionIds
                .Select(id => new MinecraftVersion { Id = id, Type = "release" })
                .ToList();
        }

        public string DisplayName { get; }

        public Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            return Task.FromResult(_versions);
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


