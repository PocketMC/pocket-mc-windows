using System;
using System.IO;
using System.Threading.Tasks;
using PocketMC.Infrastructure.Marketplace;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Marketplace
{
    public class AddonManifestOverwriteTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pocketmc-manifest-" + Guid.NewGuid().ToString("N"));

        public AddonManifestOverwriteTests()
        {
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public async Task SaveManifestAsync_RefusesToReplaceAnUnreadableManifest()
        {
            var service = new AddonManifestService();
            string path = Path.Combine(_tempDir, "addon_manifest.json");
            const string corrupt = "{ this is not json";
            await File.WriteAllTextAsync(path, corrupt);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveManifestAsync(_tempDir, new AddonManifest()));

            Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task SaveManifestAsync_WritesWhenNoManifestExists()
        {
            var service = new AddonManifestService();
            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry { FileName = "example.jar", ProjectId = "abc" });

            await service.SaveManifestAsync(_tempDir, manifest);

            var loaded = await service.LoadManifestAsync(_tempDir);
            Assert.Single(loaded.Entries);
        }
    }
}
