using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests;

public sealed class AddonManifestTests : IDisposable
{
    private readonly string _addonsDir = Path.Combine(Path.GetTempPath(), "PocketMC.AddonManifestTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SyncManifestAsync_ShouldRemoveDeletedFiles()
    {
        Directory.CreateDirectory(_addonsDir);
        var manifest = new AddonManifest();

        await manifest.SaveAsync(_addonsDir, new[]
        {
            new AddonManifestEntry
            {
                FileName = "mod-a.jar",
                ProjectId = "mod-a",
                Provider = "Modrinth",
                VersionId = "",
                InstalledAt = DateTime.MinValue
            }
        });

        IReadOnlyList<AddonManifestEntry> synced = await manifest.SyncManifestAsync(_addonsDir);

        Assert.Empty(synced);
    }

    public void Dispose()
    {
        if (Directory.Exists(_addonsDir))
        {
            Directory.Delete(_addonsDir, recursive: true);
        }
    }
}
