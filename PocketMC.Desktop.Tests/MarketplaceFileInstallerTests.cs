using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Tests;

public sealed class MarketplaceFileInstallerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.MarketplaceDownloads", Guid.NewGuid().ToString("N"));

    public MarketplaceFileInstallerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task FailedDownload_LeavesExistingLiveAddonUntouched()
    {
        string liveFile = Path.Combine(_tempDirectory, "mods", "addon.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(liveFile)!);
        await File.WriteAllTextAsync(liveFile, "existing");

        MarketplaceFileInstaller installer = CreateInstaller(_ => throw new HttpRequestException("download failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync("https://example.test/addon.jar", liveFile, null, null));

        Assert.Equal("existing", await File.ReadAllTextAsync(liveFile));
    }

    [Fact]
    public async Task SuccessfulDownload_PromotesFinalFile()
    {
        string liveFile = Path.Combine(_tempDirectory, "mods", "addon.jar");
        byte[] payload = Encoding.UTF8.GetBytes("new addon");
        MarketplaceFileInstaller installer = CreateInstaller(_ => MarketplaceHttpResponses.Bytes(payload));

        await installer.InstallAsync("https://example.test/addon.jar", liveFile, null, null);

        Assert.Equal("new addon", await File.ReadAllTextAsync(liveFile));
        Assert.False(File.Exists(liveFile + ".partial"));
    }

    [Fact]
    public async Task HashMismatch_RejectsFileAndDoesNotOverwriteExistingAddon()
    {
        string liveFile = Path.Combine(_tempDirectory, "mods", "addon.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(liveFile)!);
        await File.WriteAllTextAsync(liveFile, "existing");

        MarketplaceFileInstaller installer = CreateInstaller(_ => MarketplaceHttpResponses.Bytes(Encoding.UTF8.GetBytes("tampered")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync("https://example.test/addon.jar", liveFile, new string('0', 128), "sha512"));

        Assert.Equal("existing", await File.ReadAllTextAsync(liveFile));
    }

    [Fact]
    public async Task HashMatch_AllowsPromotion()
    {
        string liveFile = Path.Combine(_tempDirectory, "mods", "addon.jar");
        byte[] payload = Encoding.UTF8.GetBytes("trusted");
        string hash = Convert.ToHexString(SHA512.HashData(payload)).ToLowerInvariant();
        MarketplaceFileInstaller installer = CreateInstaller(_ => MarketplaceHttpResponses.Bytes(payload));

        await installer.InstallAsync("https://example.test/addon.jar", liveFile, hash, "sha512");

        Assert.Equal("trusted", await File.ReadAllTextAsync(liveFile));
    }

    [Fact]
    public async Task MissingHash_DoesNotCrashInstall()
    {
        string liveFile = Path.Combine(_tempDirectory, "mods", "addon.jar");
        MarketplaceFileInstaller installer = CreateInstaller(_ => MarketplaceHttpResponses.Bytes(Encoding.UTF8.GetBytes("unchecked")));

        await installer.InstallAsync("https://example.test/addon.jar", liveFile, null, null);

        Assert.Equal("unchecked", await File.ReadAllTextAsync(liveFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static MarketplaceFileInstaller CreateInstaller(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new MarketplaceTestHttpClientFactory(() =>
            new HttpClient(new MarketplaceDelegateHttpMessageHandler((request, _) => responder(request))));
        var downloader = new DownloaderService(factory, NullLogger<DownloaderService>.Instance);
        return new MarketplaceFileInstaller(downloader, NullLogger<MarketplaceFileInstaller>.Instance);
    }
}
