using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests;

public sealed class BedrockAddonInstallerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "PocketMC.BedrockAddonTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallAsync_UsesLevelNameWorldDirectory_WhenConfigured()
    {
        string serverDir = CreateServerDirectory("level-name=MyServer");
        string addonArchive = CreateAddonArchive();
        var installer = new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance);

        await installer.InstallAsync(addonArchive, serverDir);

        string configuredWorldJson = Path.Combine(serverDir, "worlds", "MyServer", "world_behavior_packs.json");
        string defaultWorldJson = Path.Combine(serverDir, "worlds", "Bedrock level", "world_behavior_packs.json");

        Assert.True(File.Exists(configuredWorldJson));
        Assert.False(File.Exists(defaultWorldJson));
    }

    [Fact]
    public async Task InstallAsync_UsesDefaultWorldDirectory_WhenLevelNameMissing()
    {
        string serverDir = CreateServerDirectory("# no level-name configured");
        string addonArchive = CreateAddonArchive();
        var installer = new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance);

        await installer.InstallAsync(addonArchive, serverDir);

        string defaultWorldJson = Path.Combine(serverDir, "worlds", "Bedrock level", "world_behavior_packs.json");
        Assert.True(File.Exists(defaultWorldJson));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private string CreateServerDirectory(string serverPropertiesContent)
    {
        string serverDir = Path.Combine(_rootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(serverDir, "server.properties"), serverPropertiesContent + Environment.NewLine);
        return serverDir;
    }

    private string CreateAddonArchive()
    {
        string addonDir = Path.Combine(_rootPath, "addon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(addonDir);
        string packDir = Path.Combine(addonDir, "SamplePack");
        Directory.CreateDirectory(packDir);

        File.WriteAllText(
            Path.Combine(packDir, "manifest.json"),
            """
            {
              "format_version": 2,
              "header": {
                "name": "Sample Pack",
                "uuid": "11111111-2222-3333-4444-555555555555",
                "version": [1, 0, 0]
              },
              "modules": [
                {
                  "type": "data",
                  "uuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "version": [1, 0, 0]
                }
              ]
            }
            """);

        string archivePath = Path.Combine(_rootPath, Guid.NewGuid().ToString("N") + ".mcpack");
        ZipFile.CreateFromDirectory(addonDir, archivePath);
        return archivePath;
    }
}
