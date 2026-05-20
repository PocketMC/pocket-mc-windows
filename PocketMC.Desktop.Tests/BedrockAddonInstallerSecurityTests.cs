using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests;

public sealed class BedrockAddonInstallerSecurityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallAsync_DoesNotDeletePackRoot_WhenManifestNameSanitizesToEmpty()
    {
        Directory.CreateDirectory(_tempDirectory);
        string serverDir = Path.Combine(_tempDirectory, "server");
        string behaviorPacksDir = Path.Combine(serverDir, "behavior_packs");
        Directory.CreateDirectory(behaviorPacksDir);
        string canaryPath = Path.Combine(behaviorPacksDir, "canary.txt");
        await File.WriteAllTextAsync(canaryPath, "still here");

        string addonPath = Path.Combine(_tempDirectory, "bad.mcpack");
        using (var archive = ZipFile.Open(addonPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("manifest.json");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync("""
            {
              "header": {
                "name": "...",
                "uuid": "d15c1f4d-a87f-4edc-8af1-2b5683cb6698",
                "version": [1, 0, 0]
              },
              "modules": [
                { "type": "data" }
              ]
            }
            """);
        }

        var installer = new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(addonPath, serverDir));
        Assert.True(Directory.Exists(behaviorPacksDir));
        Assert.True(File.Exists(canaryPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
