using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Services.Instances;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Marketplace;

namespace PocketMC.Infrastructure.Tests.Marketplace;

public sealed class DataPackInstallFlowTests : IDisposable
{
    private readonly string _server = Path.Combine(Path.GetTempPath(), "pmc-dpf-" + Guid.NewGuid().ToString("N"));
    private string World => Path.Combine(_server, "world");
    private readonly AddonManifestService _manifest = new();

    public DataPackInstallFlowTests()
    {
        Directory.CreateDirectory(Path.Combine(World, "datapacks"));
        Directory.CreateDirectory(Path.Combine(World, "datapacks.disabled"));
    }

    public void Dispose() => Directory.Delete(_server, true);

    private sealed class StubProvider(MarketplaceVersion latest) : IAddonProvider
    {
        public string Name => "Modrinth";
        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader) => Task.FromResult<MarketplaceVersion?>(latest);
        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates) => Task.FromResult<MarketplaceVersion?>(latest);
        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId) => Task.FromResult<MarketplaceVersion?>(null);
        public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId) => Task.FromResult<MarketplaceProjectInfo?>(null);
    }

    private static MarketplaceVersion Version(string id, string fileName) => new()
    {
        Id = id, ProjectId = "proj", FileName = fileName, DownloadUrl = "https://example.test/" + fileName
    };

    private DataPackInstallService Create(MarketplaceVersion latest, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new MarketplaceTestHttpClientFactory(() =>
            new HttpClient(new MarketplaceDelegateHttpMessageHandler((request, _) => responder(request))));
        var installer = new MarketplaceFileInstaller(
            new DownloaderService(factory, NullLogger<DownloaderService>.Instance),
            NullLogger<MarketplaceFileInstaller>.Instance);
        return new DataPackInstallService(new[] { new StubProvider(latest) }, installer, _manifest);
    }

    private static HttpResponseMessage Ok(string content) => MarketplaceHttpResponses.Bytes(Encoding.UTF8.GetBytes(content));

    private async Task SeedInstalledAsync(string fileName, bool enabled = true)
    {
        string dir = Path.Combine(World, enabled ? "datapacks" : "datapacks.disabled");
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), "old");
        await _manifest.RegisterInstallAsync(_server, "Modrinth", "proj", "v1", fileName);
    }

    [Fact]
    public async Task Update_WithNewFileName_ReplacesOldPackAndManifestEntry()
    {
        await SeedInstalledAsync("old.zip");
        var latest = Version("v2", "new.zip");

        string name = await Create(latest, _ => Ok("new")).UpdateAsync(_server, World, "old.zip", latest, "1.21");

        Assert.Equal("new.zip", name);
        Assert.Equal(new[] { "new.zip" }, DataPackLibrary.List(World).Select(p => p.Name));
        var entry = Assert.Single((await _manifest.LoadManifestAsync(_server)).Entries);
        Assert.Equal(("new.zip", "v2"), (entry.FileName, entry.VersionId));
    }

    [Fact]
    public async Task Update_WithSameFileName_ReplacesInPlace()
    {
        await SeedInstalledAsync("pack.zip");
        var latest = Version("v2", "pack.zip");

        await Create(latest, _ => Ok("new")).UpdateAsync(_server, World, "pack.zip", latest, "1.21");

        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(World, "datapacks", "pack.zip")));
        Assert.Equal("v2", Assert.Single((await _manifest.LoadManifestAsync(_server)).Entries).VersionId);
    }

    [Fact]
    public async Task Update_WhenDownloadFails_KeepsOldPackAndManifest()
    {
        await SeedInstalledAsync("old.zip");
        var latest = Version("v2", "new.zip");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Create(latest, _ => throw new HttpRequestException("offline")).UpdateAsync(_server, World, "old.zip", latest, "1.21"));

        Assert.Equal(new[] { "old.zip" }, DataPackLibrary.List(World).Select(p => p.Name));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(World, "datapacks", "old.zip")));
        Assert.Equal("v1", Assert.Single((await _manifest.LoadManifestAsync(_server)).Entries).VersionId);
    }

    [Fact]
    public async Task Update_KeepsDisabledPackDisabled()
    {
        await SeedInstalledAsync("old.zip", enabled: false);
        var latest = Version("v2", "new.zip");

        await Create(latest, _ => Ok("new")).UpdateAsync(_server, World, "old.zip", latest, "1.21");

        var pack = Assert.Single(DataPackLibrary.List(World));
        Assert.Equal(("new.zip", false), (pack.Name, pack.IsEnabled));
    }

    [Fact]
    public async Task Update_RefusesToOverwriteADifferentPackWithTheSameName()
    {
        await SeedInstalledAsync("old.zip");
        await File.WriteAllTextAsync(Path.Combine(World, "datapacks", "new.zip"), "other pack");
        var latest = Version("v2", "new.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(latest, _ => Ok("new")).UpdateAsync(_server, World, "old.zip", latest, "1.21"));

        Assert.Equal("other pack", await File.ReadAllTextAsync(Path.Combine(World, "datapacks", "new.zip")));
        Assert.True(File.Exists(Path.Combine(World, "datapacks", "old.zip")));
    }

    [Fact]
    public async Task Install_RefusesToOverwriteAnExistingPack()
    {
        await SeedInstalledAsync("pack.zip", enabled: false);
        var latest = Version("v2", "pack.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(latest, _ => Ok("new")).InstallAsync(_server, World, "Modrinth", "proj", "1.21"));

        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(World, "datapacks.disabled", "pack.zip")));
    }

    [Fact]
    public async Task Remove_DeletesPackAndManifestEntry()
    {
        await SeedInstalledAsync("pack.zip");

        await Create(Version("v1", "pack.zip"), _ => Ok("x")).RemoveAsync(_server, World, "pack.zip");

        Assert.Empty(DataPackLibrary.List(World));
        Assert.Empty((await _manifest.LoadManifestAsync(_server)).Entries);
    }
}
