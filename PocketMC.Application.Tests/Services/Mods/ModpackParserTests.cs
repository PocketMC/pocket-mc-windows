using PocketMC.Application.Services.Mods;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace PocketMC.Application.Tests.Services.Mods;

public sealed class ModpackParserTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ParseModrinthPack_MostlyClientOnlyPack_KeepsServerFiles()
    {
        Directory.CreateDirectory(_tempDirectory);
        string zipPath = Path.Combine(_tempDirectory, "client-heavy.mrpack");

        string modrinthIndex = """
        {
            "formatVersion": 1,
            "game": "minecraft",
            "name": "ClientHeavyPack",
            "versionId": "1.0.0",
            "dependencies": {
                "minecraft": "1.21.1",
                "fabric-loader": "0.16.0"
            },
            "files": [
                {
                    "path": "mods/server-mod.jar",
                    "downloads": ["https://example.com/server-mod.jar"],
                    "hashes": { "sha512": "abc123" },
                    "env": { "client": "required", "server": "required" },
                    "fileSize": 1024
                },
                {
                    "path": "mods/client-a.jar",
                    "downloads": ["https://example.com/client-a.jar"],
                    "hashes": { "sha512": "def456" },
                    "env": { "client": "required", "server": "unsupported" },
                    "fileSize": 1024
                },
                {
                    "path": "mods/client-b.jar",
                    "downloads": ["https://example.com/client-b.jar"],
                    "hashes": { "sha512": "ghi789" },
                    "env": { "client": "required", "server": "unsupported" },
                    "fileSize": 1024
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

        Assert.Single(result.Mods);
        Assert.Equal("server-mod.jar", result.Mods[0].Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
