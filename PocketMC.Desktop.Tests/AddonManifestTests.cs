using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Models;
using Xunit;

namespace PocketMC.Desktop.Tests
{
    public class AddonManifestTests : IDisposable
    {
        private readonly string _tempDir;

        public AddonManifestTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PocketMC_Test_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public async Task SyncManifestAsync_ShouldRemoveDeletedFiles()
        {
            // Arrange
            var service = new AddonManifestService();
            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry 
            { 
                ProjectId = "mod-a", 
                FileName = "mod-a.jar", 
                Provider = "Modrinth" 
            });
            
            // Save manifest but don't create the file
            string manifestPath = Path.Combine(_tempDir, "addon_manifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

            // Act
            await service.SyncManifestAsync(_tempDir, null!, new EngineCompatibility("Fabric"));
            var updated = await service.LoadManifestAsync(_tempDir);

            // Assert
            Assert.Empty(updated.Entries);
        }

        [Fact]
        public async Task SyncManifestAsync_ShouldKeepExistingFiles()
        {
            // Arrange
            var service = new AddonManifestService();
            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry 
            { 
                ProjectId = "mod-a", 
                FileName = "mod-a.jar", 
                Provider = "Modrinth" 
            });
            
            Directory.CreateDirectory(Path.Combine(_tempDir, "mods"));
            File.WriteAllText(Path.Combine(_tempDir, "mods", "mod-a.jar"), "dummy");
            
            string manifestPath = Path.Combine(_tempDir, "addon_manifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

            // Act
            await service.SyncManifestAsync(_tempDir, null!, new EngineCompatibility("Fabric"));
            var updated = await service.LoadManifestAsync(_tempDir);

            // Assert
            Assert.Single(updated.Entries);
            Assert.Equal("mod-a", updated.Entries[0].ProjectId);
        }

        [Fact]
        public async Task IsInstalledAsync_ReturnsFalseAndCleansEntry_WhenManifestFileNameEscapesAddonDirectory()
        {
            var service = new AddonManifestService();
            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry
            {
                ProjectId = "mod-a",
                FileName = Path.Combine("..", "outside.jar"),
                Provider = "Modrinth"
            });

            Directory.CreateDirectory(Path.Combine(_tempDir, "mods"));
            File.WriteAllText(Path.Combine(_tempDir, "outside.jar"), "not an installed addon");
            await service.SaveManifestAsync(_tempDir, manifest);

            bool installed = await service.IsInstalledAsync(
                _tempDir,
                "Modrinth",
                "mod-a",
                new EngineCompatibility("Fabric"));

            Assert.False(installed);
            AddonManifest updated = await service.LoadManifestAsync(_tempDir);
            Assert.Empty(updated.Entries);
        }

        [Fact]
        public async Task LoadManifestAsync_BackwardCompatibility_LoadsWithoutNewFields()
        {
            var service = new AddonManifestService();
            string json = @"{
                ""Entries"": [
                    {
                        ""Provider"": ""Modrinth"",
                        ""ProjectId"": ""mod-b"",
                        ""VersionId"": ""v2"",
                        ""FileName"": ""mod-b.jar"",
                        ""InstalledAt"": ""2026-05-25T00:00:00Z""
                    }
                ]
            }";
            string manifestPath = Path.Combine(_tempDir, "addon_manifest.json");
            File.WriteAllText(manifestPath, json);

            var manifest = await service.LoadManifestAsync(_tempDir);

            Assert.Single(manifest.Entries);
            var entry = manifest.Entries[0];
            Assert.Equal("mod-b", entry.ProjectId);
            Assert.Null(entry.ProjectTitle);
            Assert.Null(entry.ProjectSlug);
            Assert.Null(entry.IconUrl);
            Assert.Null(entry.DisplayName);
        }

        [Fact]
        public async Task RegisterInstallAsync_SavesNewFieldsCorrectly()
        {
            var service = new AddonManifestService();

            await service.RegisterInstallAsync(_tempDir, "Modrinth", "mod-c", "v3", "mod-c.jar", "My Mod", "http://icon.url", "My Mod Display");

            var manifest = await service.LoadManifestAsync(_tempDir);
            Assert.Single(manifest.Entries);
            var entry = manifest.Entries[0];
            Assert.Equal("mod-c", entry.ProjectId);
            Assert.Equal("My Mod", entry.ProjectTitle);
            Assert.Equal("http://icon.url", entry.IconUrl);
            Assert.Equal("My Mod Display", entry.DisplayName);
        }

        [Fact]
        public async Task UpdateManifestFileNameAsync_UpdatesNameCorrectly()
        {
            var service = new AddonManifestService();
            await service.RegisterInstallAsync(_tempDir, "Modrinth", "mod-d", "v4", "mod-d.jar", "My Mod D", null, null);

            await service.UpdateManifestFileNameAsync(_tempDir, "mod-d.jar", "mod-d.jar.disabled");

            var manifest = await service.LoadManifestAsync(_tempDir);
            Assert.Single(manifest.Entries);
            Assert.Equal("mod-d.jar.disabled", manifest.Entries[0].FileName);
        }
    }
}
