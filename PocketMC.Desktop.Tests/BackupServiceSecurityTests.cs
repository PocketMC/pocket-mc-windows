using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Backups;

namespace PocketMC.Desktop.Tests;

public sealed class BackupServiceSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC_BackupSecurity_" + Guid.NewGuid());

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
    public void VerifyBackupIntegrity_ReturnsFalse_WhenManifestFileNameEscapesBackupsDirectory()
    {
        string serverDir = Path.Combine(_root, "server");
        Directory.CreateDirectory(Path.Combine(serverDir, "backups"));

        string escapedFileName = Path.Combine("..", "outside.zip");
        string outsidePath = Path.Combine(serverDir, "outside.zip");
        File.WriteAllText(outsidePath, "outside backup");

        var manifest = new BackupManifest();
        manifest.Entries.Add(new BackupMetadataEntry
        {
            FileName = escapedFileName,
            Sha256Checksum = ComputeSha256(outsidePath)
        });
        manifest.Save(serverDir);

        var service = new BackupService(null!, null!, null!, null!, NullLogger<BackupService>.Instance);

        Assert.False(service.VerifyBackupIntegrity(serverDir, escapedFileName));
    }

    private static string ComputeSha256(string filePath)
    {
        byte[] hash = SHA256.HashData(File.ReadAllBytes(filePath));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
