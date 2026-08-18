using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Mods;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Mods
{
    public sealed class BedrockAddonInstallerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly BedrockAddonInstaller _installer;

        public BedrockAddonInstallerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"pocketmc-bds-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _installer = new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance);
        }

        [Fact]
        public async Task InstallAddonAsync_ExtractsNestedMcpacksInsideMcaddon()
        {
            string serverDir = CreateServerDir("Survival");
            string mcaddonPath = Path.Combine(_tempDir, "DualPack.mcaddon");

            // Create BP inner .mcpack
            string bpPackPath = Path.Combine(_tempDir, "pack_bp.mcpack");
            CreateZipArchive(bpPackPath, new()
            {
                ["manifest.json"] = """
                {
                  "header": {
                    "name": "My Custom BP",
                    "description": "Custom entities and recipes",
                    "uuid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    "version": [1, 2, 0]
                  },
                  "modules": [{ "type": "data" }]
                }
                """,
                ["pack_icon.png"] = "fake-png-data"
            });

            // Create RP inner .mcpack
            string rpPackPath = Path.Combine(_tempDir, "pack_rp.mcpack");
            CreateZipArchive(rpPackPath, new()
            {
                ["manifest.json"] = """
                {
                  "header": {
                    "name": "My Custom RP",
                    "description": "Custom textures and models",
                    "uuid": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                    "version": [1, 2, 0]
                  },
                  "modules": [{ "type": "resources" }]
                }
                """,
                ["pack_icon.png"] = "fake-png-data"
            });

            // Wrap both into outer .mcaddon container
            using (var outer = ZipFile.Open(mcaddonPath, ZipArchiveMode.Create))
            {
                outer.CreateEntryFromFile(bpPackPath, "pack_bp.mcpack");
                outer.CreateEntryFromFile(rpPackPath, "pack_rp.mcpack");
            }

            // Ingest outer .mcaddon
            var installed = await _installer.InstallAddonAsync(mcaddonPath, serverDir);

            Assert.Equal(2, installed.Count);
            Assert.Contains(installed, p => p.PackType == BedrockPackType.Behavior && p.Uuid == "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            Assert.Contains(installed, p => p.PackType == BedrockPackType.Resource && p.Uuid == "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

            // Verify active world registration
            string worldBpJson = Path.Combine(serverDir, "worlds", "Survival", "world_behavior_packs.json");
            string worldRpJson = Path.Combine(serverDir, "worlds", "Survival", "world_resource_packs.json");
            Assert.True(File.Exists(worldBpJson));
            Assert.True(File.Exists(worldRpJson));

            string bpText = await File.ReadAllTextAsync(worldBpJson);
            string rpText = await File.ReadAllTextAsync(worldRpJson);
            Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", bpText);
            Assert.Contains("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", rpText);
        }

        [Fact]
        public async Task InstallAddonAsync_ParsesManifestWithCommentsAndTrailingCommas()
        {
            string serverDir = CreateServerDir("CreativeWorld");
            string packPath = Path.Combine(_tempDir, "CommentPack.mcpack");

            CreateZipArchive(packPath, new()
            {
                ["manifest.json"] = """
                // Bedrock manifest with comments
                {
                  /* multi-line comment header */
                  "header": {
                    "name": "Lenient JSON Pack",
                    "description": "Tested for edge-case JSON syntax",
                    "uuid": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                    "version": [2, 0, 1,], // trailing comma in array
                  },
                  "modules": [
                    { "type": "data", },
                  ],
                }
                """
            });

            var installed = await _installer.InstallAddonAsync(packPath, serverDir);
            Assert.Single(installed);
            Assert.Equal("Lenient JSON Pack", installed[0].Name);
            Assert.Equal("2.0.1", installed[0].Version);
            Assert.Equal(BedrockPackType.Behavior, installed[0].PackType);
        }

        [Fact]
        public async Task SetPackEnabledAsync_TogglesWithoutDeletingFiles()
        {
            string serverDir = CreateServerDir("ToggleWorld");
            string packPath = Path.Combine(_tempDir, "TogglePack.mcpack");

            CreateZipArchive(packPath, new()
            {
                ["manifest.json"] = """
                {
                  "header": {
                    "name": "Toggle Me",
                    "uuid": "dddddddd-dddd-dddd-dddd-dddddddddddd",
                    "version": [1, 0, 0]
                  },
                  "modules": [{ "type": "data" }]
                }
                """
            });

            await _installer.InstallAddonAsync(packPath, serverDir);

            // Initially enabled
            var packs = _installer.GetPacks(serverDir);
            Assert.Single(packs);
            Assert.True(packs[0].IsEnabled);

            // Disable
            await _installer.SetPackEnabledAsync(serverDir, "dddddddd-dddd-dddd-dddd-dddddddddddd", BedrockPackType.Behavior, isEnabled: false);
            packs = _installer.GetPacks(serverDir);
            Assert.Single(packs);
            Assert.False(packs[0].IsEnabled);

            // Verify files on disk still exist
            Assert.True(Directory.Exists(packs[0].DirectoryPath));

            // Re-enable
            await _installer.SetPackEnabledAsync(serverDir, "dddddddd-dddd-dddd-dddd-dddddddddddd", BedrockPackType.Behavior, isEnabled: true);
            packs = _installer.GetPacks(serverDir);
            Assert.True(packs[0].IsEnabled);
        }

        [Fact]
        public async Task ReorderPackAsync_SwapsOrderInWorldJson()
        {
            string serverDir = CreateServerDir("ReorderWorld");
            string pack1 = Path.Combine(_tempDir, "Pack1.mcpack");
            string pack2 = Path.Combine(_tempDir, "Pack2.mcpack");

            CreateZipArchive(pack1, new()
            {
                ["manifest.json"] = """
                {
                  "header": { "name": "Pack 1", "uuid": "11111111-1111-1111-1111-111111111111", "version": [1, 0, 0] },
                  "modules": [{ "type": "resources" }]
                }
                """
            });

            CreateZipArchive(pack2, new()
            {
                ["manifest.json"] = """
                {
                  "header": { "name": "Pack 2", "uuid": "22222222-2222-2222-2222-222222222222", "version": [1, 0, 0] },
                  "modules": [{ "type": "resources" }]
                }
                """
            });

            await _installer.InstallAddonAsync(pack1, serverDir);
            await _installer.InstallAddonAsync(pack2, serverDir);

            string worldRpJson = Path.Combine(serverDir, "worlds", "ReorderWorld", "world_resource_packs.json");
            var entries = JsonNode.Parse(await File.ReadAllTextAsync(worldRpJson))!.AsArray();
            Assert.Equal("11111111-1111-1111-1111-111111111111", entries[0]!["pack_id"]!.GetValue<string>());
            Assert.Equal("22222222-2222-2222-2222-222222222222", entries[1]!["pack_id"]!.GetValue<string>());

            // Move Pack 2 up
            await _installer.ReorderPackAsync(serverDir, "22222222-2222-2222-2222-222222222222", BedrockPackType.Resource, moveUp: true);

            entries = JsonNode.Parse(await File.ReadAllTextAsync(worldRpJson))!.AsArray();
            Assert.Equal("22222222-2222-2222-2222-222222222222", entries[0]!["pack_id"]!.GetValue<string>());
            Assert.Equal("11111111-1111-1111-1111-111111111111", entries[1]!["pack_id"]!.GetValue<string>());
        }

        [Fact]
        public async Task DeletePackAsync_RemovesFromWorldJsonAndDeletesFromDisk()
        {
            string serverDir = CreateServerDir("DeleteWorld");
            string packPath = Path.Combine(_tempDir, "DeletePack.mcpack");

            CreateZipArchive(packPath, new()
            {
                ["manifest.json"] = """
                {
                  "header": { "name": "To Be Deleted", "uuid": "33333333-3333-3333-3333-333333333333", "version": [1, 0, 0] },
                  "modules": [{ "type": "data" }]
                }
                """
            });

            var installed = await _installer.InstallAddonAsync(packPath, serverDir);
            string diskPath = installed[0].DirectoryPath;
            Assert.True(Directory.Exists(diskPath));

            await _installer.DeletePackAsync(serverDir, "33333333-3333-3333-3333-333333333333", BedrockPackType.Behavior);

            Assert.False(Directory.Exists(diskPath));
            string worldBpJson = Path.Combine(serverDir, "worlds", "DeleteWorld", "world_behavior_packs.json");
            string json = await File.ReadAllTextAsync(worldBpJson);
            Assert.DoesNotContain("33333333-3333-3333-3333-333333333333", json);
        }

        private string CreateServerDir(string levelName)
        {
            string dir = Path.Combine(_tempDir, $"server-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "server.properties"), new[]
            {
                $"level-name={levelName}",
                "server-port=19132"
            });
            Directory.CreateDirectory(Path.Combine(dir, "worlds", levelName));
            return dir;
        }

        private static void CreateZipArchive(string targetZipPath, System.Collections.Generic.Dictionary<string, string> files)
        {
            using var archive = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);
            foreach (var (filename, content) in files)
            {
                var entry = archive.CreateEntry(filename);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(content);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }
    }
}
