using PocketMC.Application.Services.Mods;
using PocketMC.Domain.Storage;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace PocketMC.Application.Tests.Services.Mods;

/// <summary>
/// Tests that ModpackService correctly rejects modpack files containing
/// path-traversal entries (zip-slip attacks) in override directories.
/// </summary>
public sealed class ModpackPathTraversalTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ParseModrinthPack_SkipsModsWithPathTraversal()
    {
        Directory.CreateDirectory(_tempDirectory);
        string zipPath = Path.Combine(_tempDirectory, "malicious-modrinth.mrpack");

        // Build a minimal Modrinth modpack index with a path-traversal mod entry
        string modrinthIndex = """
        {
            "formatVersion": 1,
            "game": "minecraft",
            "name": "MaliciousPack",
            "versionId": "1.0.0",
            "dependencies": {
                "minecraft": "1.20.1",
                "fabric-loader": "0.15.7"
            },
            "files": [
                {
                    "path": "mods/safe-mod.jar",
                    "downloads": ["https://example.com/safe-mod.jar"],
                    "hashes": { "sha1": "abc123" },
                    "fileSize": 1024
                },
                {
                    "path": "../../malware.exe",
                    "downloads": ["https://evil.com/malware.exe"],
                    "hashes": { "sha1": "def456" },
                    "fileSize": 2048
                },
                {
                    "path": "mods/../../../escape.dll",
                    "downloads": ["https://evil.com/escape.dll"],
                    "hashes": { "sha1": "ghi789" },
                    "fileSize": 512
                }
            ]
        }
        """;

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modrinth.index.json");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(modrinthIndex);
        }

        // Parse the modpack — should only include the safe mod
        var parser = new ModpackParser(NullLogger<ModpackParser>.Instance);
        var result = await parser.ParseZipAsync(zipPath);

        Assert.Equal("MaliciousPack", result.Name);
        Assert.Equal("Fabric", result.Loader);

        // Only the safe mod should survive the path traversal filter
        Assert.Single(result.Mods);
        Assert.Equal("safe-mod.jar", result.Mods[0].Name);
        Assert.Equal("mods/safe-mod.jar", result.Mods[0].DestinationPath);
    }

    [Fact]
    public async Task ParseModrinthPack_AllowsNestedSubdirectoryPaths()
    {
        Directory.CreateDirectory(_tempDirectory);
        string zipPath = Path.Combine(_tempDirectory, "nested-modrinth.mrpack");

        string modrinthIndex = """
        {
            "formatVersion": 1,
            "game": "minecraft",
            "name": "NestedPack",
            "versionId": "1.0.0",
            "dependencies": {
                "minecraft": "1.20.1",
                "fabric-loader": "0.15.7"
            },
            "files": [
                {
                    "path": "mods/fabric/worldedit.jar",
                    "downloads": ["https://example.com/worldedit.jar"],
                    "hashes": { "sha1": "abc123" },
                    "fileSize": 1024
                },
                {
                    "path": "config/worldedit/config.yml",
                    "downloads": ["https://example.com/config.yml"],
                    "hashes": { "sha1": "def456" },
                    "fileSize": 256
                }
            ]
        }
        """;

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modrinth.index.json");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(modrinthIndex);
        }

        var parser = new ModpackParser(NullLogger<ModpackParser>.Instance);
        var result = await parser.ParseZipAsync(zipPath);

        // Both legitimate nested paths should be kept
        Assert.Equal(2, result.Mods.Count);
    }



    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}