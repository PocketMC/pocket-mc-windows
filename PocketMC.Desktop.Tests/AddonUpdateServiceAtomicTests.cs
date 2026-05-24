using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class AddonUpdateServiceAtomicTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.AddonUpdates", Guid.NewGuid().ToString("N"));

    public AddonUpdateServiceAtomicTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SameFilenameUpdate_DoesNotOverwriteUntilDownloadCompletes()
    {
        string serverDir = CreateServerWithAddon("same.jar", "old", "old-version");
        AddonUpdateService service = CreateService(_ => throw new HttpRequestException("network failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyUpdateAsync(
                serverDir,
                "same.jar",
                UpdateInfo("new-version", "same.jar", "https://example.test/same.jar"),
                "Modrinth",
                "addon",
                new EngineCompatibility("Fabric")));

        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(serverDir, "mods", "same.jar")));
        AddonManifest manifest = await new AddonManifestService().LoadManifestAsync(serverDir);
        AddonManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("old-version", entry.VersionId);
    }

    [Fact]
    public async Task RenamedUpdate_DoesNotDeleteOldFileWhenNewDownloadFails()
    {
        string serverDir = CreateServerWithAddon("old.jar", "old", "old-version");
        AddonUpdateService service = CreateService(_ => throw new HttpRequestException("network failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyUpdateAsync(
                serverDir,
                "old.jar",
                UpdateInfo("new-version", "new.jar", "https://example.test/new.jar"),
                "Modrinth",
                "addon",
                new EngineCompatibility("Fabric")));

        Assert.True(File.Exists(Path.Combine(serverDir, "mods", "old.jar")));
        Assert.False(File.Exists(Path.Combine(serverDir, "mods", "new.jar")));
    }

    [Fact]
    public async Task ManifestUpdatesOnlyAfterFilePromotionSucceeds()
    {
        string serverDir = CreateServerWithAddon("old.jar", "old", "old-version");
        byte[] payload = Encoding.UTF8.GetBytes("new");
        string hash = Convert.ToHexString(SHA512.HashData(payload)).ToLowerInvariant();
        AddonUpdateService service = CreateService(_ => MarketplaceHttpResponses.Bytes(payload));

        await service.ApplyUpdateAsync(
            serverDir,
            "old.jar",
            UpdateInfo("new-version", "new.jar", "https://example.test/new.jar", hash, "sha512"),
            "Modrinth",
            "addon",
            new EngineCompatibility("Fabric"));

        Assert.False(File.Exists(Path.Combine(serverDir, "mods", "old.jar")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(serverDir, "mods", "new.jar")));

        AddonManifest manifest = await new AddonManifestService().LoadManifestAsync(serverDir);
        AddonManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("new-version", entry.VersionId);
        Assert.Equal("new.jar", entry.FileName);
    }

    [Fact]
    public async Task HashMismatch_DoesNotOverwriteLiveAddonOrManifest()
    {
        string serverDir = CreateServerWithAddon("same.jar", "old", "old-version");
        AddonUpdateService service = CreateService(_ => MarketplaceHttpResponses.Bytes(Encoding.UTF8.GetBytes("tampered")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyUpdateAsync(
                serverDir,
                "same.jar",
                UpdateInfo("new-version", "same.jar", "https://example.test/same.jar", new string('0', 128), "sha512"),
                "Modrinth",
                "addon",
                new EngineCompatibility("Fabric")));

        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(serverDir, "mods", "same.jar")));
        AddonManifest manifest = await new AddonManifestService().LoadManifestAsync(serverDir);
        Assert.Equal("old-version", Assert.Single(manifest.Entries).VersionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateServerWithAddon(string fileName, string content, string versionId)
    {
        string serverDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        string modsDir = Path.Combine(serverDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, fileName), content);
        new AddonManifestService().RegisterInstallAsync(serverDir, "Modrinth", "addon", versionId, fileName).GetAwaiter().GetResult();
        return serverDir;
    }

    private static AddonUpdateCheckResult UpdateInfo(
        string versionId,
        string fileName,
        string downloadUrl,
        string? hash = null,
        string? hashType = null)
    {
        return new AddonUpdateCheckResult
        {
            IsUpdateAvailable = true,
            LatestVersionId = versionId,
            LatestVersionName = versionId,
            LatestFileName = fileName,
            LatestDownloadUrl = downloadUrl,
            Hash = hash,
            HashType = hashType,
            ProjectTitle = "addon"
        };
    }

    private static AddonUpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new MarketplaceTestHttpClientFactory(() =>
            new HttpClient(new MarketplaceDelegateHttpMessageHandler((request, _) => responder(request))));
        var downloader = new DownloaderService(factory, NullLogger<DownloaderService>.Instance);
        var installer = new MarketplaceFileInstaller(downloader, NullLogger<MarketplaceFileInstaller>.Instance);
        return new AddonUpdateService(new AddonManifestService(), null!, null!, null!, factory, downloader, installer);
    }
}
