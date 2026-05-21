using PocketMC.Desktop.Features.Instances.Backups;

namespace PocketMC.Desktop.Tests;

public sealed class BackupManifestSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC_BackupManifest_" + Guid.NewGuid());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp files.
        }
    }

    [Fact]
    public void PurgeOrphanedEntries_RemovesEntriesThatEscapeBackupDirectory()
    {
        string serverDir = Path.Combine(_root, "server");
        Directory.CreateDirectory(Path.Combine(serverDir, "backups"));
        File.WriteAllText(Path.Combine(serverDir, "outside.zip"), "outside");

        var manifest = new BackupManifest();
        manifest.Entries.Add(new BackupMetadataEntry
        {
            FileName = Path.Combine("..", "outside.zip")
        });

        manifest.PurgeOrphanedEntries(serverDir);

        Assert.Empty(manifest.Entries);
    }
}
