using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Infrastructure.Backups;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Infrastructure;
using Xunit;

namespace PocketMC.Desktop.Tests;

public sealed class RestoreBackupTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "PocketMC_RestoreTests_" + Guid.NewGuid().ToString("N"));

    public RestoreBackupTests()
    {
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_SuccessfulJavaRestore_SwapsDirectoriesCorrectly()
    {
        // Arrange
        string serverDir = Path.Combine(_testRoot, "server");
        Directory.CreateDirectory(serverDir);

        string worldDir = Path.Combine(serverDir, "world");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "original level data");
        File.WriteAllText(Path.Combine(worldDir, "original_file.txt"), "some original file");

        // Create backup ZIP
        string backupDir = Path.Combine(serverDir, "backups");
        Directory.CreateDirectory(backupDir);
        string zipPath = Path.Combine(backupDir, "world-backup.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("level.dat");
            using (var stream = entry1.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("new level data");
            }

            var entry2 = archive.CreateEntry("new_file.txt");
            using (var stream = entry2.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("some new file");
            }
        }

        var metadata = new InstanceMetadata { ServerType = "Java" };
        var service = new BackupService(null!, new ServerConfigurationService(null!, new PocketMC.Infrastructure.Instances.GeyserDetector()), null!, null!, NullLogger<BackupService>.Instance);

        // Act
        await service.RestoreBackupAsync(metadata, zipPath, serverDir);

        // Assert
        Assert.True(Directory.Exists(worldDir));
        Assert.True(File.Exists(Path.Combine(worldDir, "level.dat")));
        Assert.Equal("new level data", File.ReadAllText(Path.Combine(worldDir, "level.dat")));
        Assert.True(File.Exists(Path.Combine(worldDir, "new_file.txt")));
        Assert.False(File.Exists(Path.Combine(worldDir, "original_file.txt")));

        // Check cleanup (no staging or backup folders left)
        var directories = Directory.GetDirectories(serverDir);
        Assert.DoesNotContain(directories, d => Path.GetFileName(d).StartsWith(".restore-stage-"));
        Assert.DoesNotContain(directories, d => Path.GetFileName(d).StartsWith(".restore-backup-"));
    }

    [Fact]
    public async Task RestoreBackupAsync_CorruptZip_DoesNotDeleteOriginalWorld()
    {
        // Arrange
        string serverDir = Path.Combine(_testRoot, "server");
        Directory.CreateDirectory(serverDir);

        string worldDir = Path.Combine(serverDir, "world");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "original level data");
        File.WriteAllText(Path.Combine(worldDir, "original_file.txt"), "some original file");

        string zipPath = Path.Combine(serverDir, "corrupt.zip");
        File.WriteAllText(zipPath, "corrupt file contents - not a zip");

        var metadata = new InstanceMetadata { ServerType = "Java" };
        var service = new BackupService(null!, new ServerConfigurationService(null!, new PocketMC.Infrastructure.Instances.GeyserDetector()), null!, null!, NullLogger<BackupService>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.RestoreBackupAsync(metadata, zipPath, serverDir));

        // Original world should be completely untouched
        Assert.True(Directory.Exists(worldDir));
        Assert.True(File.Exists(Path.Combine(worldDir, "level.dat")));
        Assert.Equal("original level data", File.ReadAllText(Path.Combine(worldDir, "level.dat")));
        Assert.True(File.Exists(Path.Combine(worldDir, "original_file.txt")));
    }

    [Fact]
    public async Task RestoreBackupAsync_MissingLevelDat_ThrowsAndDoesNotDeleteOriginalWorld()
    {
        // Arrange
        string serverDir = Path.Combine(_testRoot, "server");
        Directory.CreateDirectory(serverDir);

        string worldDir = Path.Combine(serverDir, "world");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "original level data");
        File.WriteAllText(Path.Combine(worldDir, "original_file.txt"), "some original file");

        string zipPath = Path.Combine(serverDir, "invalid-structure.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("random_file.txt");
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("no level.dat here");
            }
        }

        var metadata = new InstanceMetadata { ServerType = "Java" };
        var service = new BackupService(null!, new ServerConfigurationService(null!, new PocketMC.Infrastructure.Instances.GeyserDetector()), null!, null!, NullLogger<BackupService>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.RestoreBackupAsync(metadata, zipPath, serverDir));

        // Original world should be completely untouched
        Assert.True(Directory.Exists(worldDir));
        Assert.True(File.Exists(Path.Combine(worldDir, "level.dat")));
        Assert.Equal("original level data", File.ReadAllText(Path.Combine(worldDir, "level.dat")));
        Assert.True(File.Exists(Path.Combine(worldDir, "original_file.txt")));
    }

    [Fact]
    public async Task RestoreBackupAsync_SwapFailure_RollsBackToOriginalWorld()
    {
        // Arrange
        string serverDir = Path.Combine(_testRoot, "server");
        Directory.CreateDirectory(serverDir);

        string worldDir = Path.Combine(serverDir, "world");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "original level data");
        File.WriteAllText(Path.Combine(worldDir, "original_file.txt"), "some original file");

        string zipPath = Path.Combine(serverDir, "world-backup.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("level.dat");
            using (var stream = entry1.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("new level data");
            }
        }

        var metadata = new InstanceMetadata { ServerType = "Java" };
        var service = new BackupService(null!, new ServerConfigurationService(null!, new PocketMC.Infrastructure.Instances.GeyserDetector()), null!, null!, NullLogger<BackupService>.Instance);

        FileStream? lockedStream = null;
        Action<string> onProgress = (msg) =>
        {
            if (msg == "Applying restored world...")
            {
                // Find stageDir
                var stageDir = Directory.GetDirectories(serverDir)
                    .FirstOrDefault(d => Path.GetFileName(d).StartsWith(".restore-stage-"));

                if (stageDir != null)
                {
                    var fileToLock = Path.Combine(stageDir, "level.dat");
                    // Exclusively lock the file to make Directory.Move fail
                    lockedStream = new FileStream(fileToLock, FileMode.Open, FileAccess.Read, FileShare.None);
                }
            }
        };

        // Act & Assert
        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                service.RestoreBackupAsync(metadata, zipPath, serverDir, onProgress));
        }
        finally
        {
            lockedStream?.Dispose();
        }

        // Check that the rollback restored the original world
        Assert.True(Directory.Exists(worldDir));
        Assert.True(File.Exists(Path.Combine(worldDir, "level.dat")));
        Assert.Equal("original level data", File.ReadAllText(Path.Combine(worldDir, "level.dat")));
        Assert.True(File.Exists(Path.Combine(worldDir, "original_file.txt")));
    }
}



