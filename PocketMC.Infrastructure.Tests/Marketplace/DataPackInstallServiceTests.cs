using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Marketplace;

namespace PocketMC.Infrastructure.Tests.Marketplace;

public sealed class DataPackInstallServiceTests : IDisposable
{
    private readonly string _server = Path.Combine(Path.GetTempPath(), "pmc-dpi-" + Guid.NewGuid().ToString("N"));

    public DataPackInstallServiceTests() => Directory.CreateDirectory(_server);
    public void Dispose() => Directory.Delete(_server, true);

    private sealed class FakeProvider(string latestId) : IAddonProvider
    {
        public string Name => "Modrinth";
        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader) => GetLatestVersionAsync(projectId, mcVersion, new[] { loader });
        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates)
            => Task.FromResult<MarketplaceVersion?>(new MarketplaceVersion { Id = latestId, ProjectId = projectId });
        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId) => Task.FromResult<MarketplaceVersion?>(null);
        public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId) => Task.FromResult<MarketplaceProjectInfo?>(null);
    }

    private async Task<DataPackInstallService> CreateAsync(string latestId)
    {
        var manifest = new AddonManifestService();
        await manifest.RegisterInstallAsync(_server, "Modrinth", "proj", "v1", "pack.zip");
        return new DataPackInstallService(new[] { new FakeProvider(latestId) }, null!, manifest);
    }

    private sealed class LoaderRecordingProvider : IAddonProvider
    {
        public IReadOnlyList<string>? LastLoaders;
        public string Name => "Modrinth";
        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader) => GetLatestVersionAsync(projectId, mcVersion, new[] { loader });
        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates)
        {
            LastLoaders = loaderCandidates;
            return Task.FromResult<MarketplaceVersion?>(null);
        }
        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId) => Task.FromResult<MarketplaceVersion?>(null);
        public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId) => Task.FromResult<MarketplaceProjectInfo?>(null);
    }

    [Fact]
    public async Task CheckUpdate_AsksModrinthForDatapackLoader()
    {
        var manifest = new AddonManifestService();
        await manifest.RegisterInstallAsync(_server, "Modrinth", "proj", "v1", "pack.zip");
        var provider = new LoaderRecordingProvider();
        await new DataPackInstallService(new[] { provider }, null!, manifest).CheckUpdateAsync(_server, "pack.zip", "1.21");
        Assert.Equal(new[] { "datapack" }, provider.LastLoaders);
    }

    [Fact]
    public async Task CheckUpdate_ReturnsNewerVersion()
        => Assert.Equal("v2", (await (await CreateAsync("v2")).CheckUpdateAsync(_server, "pack.zip", "1.21"))?.Id);

    [Fact]
    public async Task CheckUpdate_NullWhenCurrent()
        => Assert.Null(await (await CreateAsync("v1")).CheckUpdateAsync(_server, "pack.zip", "1.21"));

    [Fact]
    public async Task CheckUpdate_NullForUntrackedPack()
        => Assert.Null(await (await CreateAsync("v2")).CheckUpdateAsync(_server, "manual.zip", "1.21"));
}
