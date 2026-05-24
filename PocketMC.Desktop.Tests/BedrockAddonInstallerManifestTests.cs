using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class BedrockAddonInstallerManifestTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.BedrockManifest", Guid.NewGuid().ToString("N"));

    public BedrockAddonInstallerManifestTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task InstallAsync_AcceptsHeaderVersionArray()
    {
        string serverDir = CreateServerDir();
        string addonPath = CreateAddon("""
        {
          "header": {
            "name": "ArrayVersion",
            "uuid": "d15c1f4d-a87f-4edc-8af1-2b5683cb6698",
            "version": [1, 0, 0]
          },
          "modules": [{ "type": "data" }]
        }
        """);

        await CreateInstaller().InstallAsync(addonPath, serverDir);

        Assert.Equal(new[] { 1, 0, 0 }, await ReadRegisteredVersionAsync(serverDir));
    }

    [Fact]
    public async Task InstallAsync_AcceptsHeaderVersionString()
    {
        string serverDir = CreateServerDir();
        string addonPath = CreateAddon("""
        {
          "header": {
            "name": "StringVersion",
            "uuid": "d15c1f4d-a87f-4edc-8af1-2b5683cb6698",
            "version": "1.0.0"
          },
          "modules": [{ "type": "data" }]
        }
        """);

        await CreateInstaller().InstallAsync(addonPath, serverDir);

        Assert.Equal(new[] { 1, 0, 0 }, await ReadRegisteredVersionAsync(serverDir));
    }

    [Fact]
    public async Task InstallAsync_MissingHeaderVersionFallsBackToOneZeroZero()
    {
        string serverDir = CreateServerDir();
        string addonPath = CreateAddon("""
        {
          "header": {
            "name": "MissingVersion",
            "uuid": "d15c1f4d-a87f-4edc-8af1-2b5683cb6698"
          },
          "modules": [{ "type": "data" }]
        }
        """);

        await CreateInstaller().InstallAsync(addonPath, serverDir);

        Assert.Equal(new[] { 1, 0, 0 }, await ReadRegisteredVersionAsync(serverDir));
    }

    [Fact]
    public async Task InstallAsync_RejectsInvalidUuid()
    {
        string serverDir = CreateServerDir();
        string addonPath = CreateAddon("""
        {
          "header": {
            "name": "BadUuid",
            "uuid": "not-a-uuid",
            "version": [1, 0, 0]
          },
          "modules": [{ "type": "data" }]
        }
        """);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateInstaller().InstallAsync(addonPath, serverDir));
    }

    [Fact]
    public void BedrockMarketplaceInstall_RejectsUnsupportedFileExtension()
    {
        Assert.Throws<NotSupportedException>(() =>
            MarketplaceDownloadPolicy.RequireCompatibleFileName("addon.jar", new EngineCompatibility("Bedrock")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateServerDir()
    {
        string serverDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(serverDir, "worlds", "Bedrock level"));
        return serverDir;
    }

    private string CreateAddon(string manifestJson)
    {
        string addonPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.mcpack");
        using var archive = ZipFile.Open(addonPath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("manifest.json");
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(manifestJson);
        return addonPath;
    }

    private static BedrockAddonInstaller CreateInstaller()
    {
        return new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance);
    }

    private static async Task<int[]> ReadRegisteredVersionAsync(string serverDir)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(serverDir, "worlds", "Bedrock level", "world_behavior_packs.json"));
        JsonArray entries = JsonNode.Parse(json)!.AsArray();
        JsonArray version = entries[0]!["version"]!.AsArray();
        return version.Select(node => node!.GetValue<int>()).ToArray();
    }
}
