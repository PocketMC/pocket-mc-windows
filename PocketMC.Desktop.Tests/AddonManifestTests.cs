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
            await service.SyncManifestAsync(_tempDir, null, new EngineCompatibility("Fabric"));
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
            await service.SyncManifestAsync(_tempDir, null, new EngineCompatibility("Fabric"));
            var updated = await service.LoadManifestAsync(_tempDir);

            // Assert
            Assert.Single(updated.Entries);
            Assert.Equal("mod-a", updated.Entries[0].ProjectId);
        }
    }
}
