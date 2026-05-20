using System.IO.Compression;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Tests;

public sealed class SafeZipExtractorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExtractAsync_Throws_WhenZipEntryEscapesDestination()
    {
        Directory.CreateDirectory(_tempDirectory);
        string zipPath = Path.Combine(_tempDirectory, "malicious.zip");
        string extractPath = Path.Combine(_tempDirectory, "extract");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../outside.txt");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("not allowed");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => SafeZipExtractor.ExtractAsync(zipPath, extractPath));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "outside.txt")));
    }

    [Fact]
    public async Task ExtractAsync_Throws_WhenZipEntryTargetsAlternateDataStream()
    {
        Directory.CreateDirectory(_tempDirectory);
        string zipPath = Path.Combine(_tempDirectory, "ads.zip");
        string extractPath = Path.Combine(_tempDirectory, "extract");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("server.properties:secret");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("not allowed");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => SafeZipExtractor.ExtractAsync(zipPath, extractPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
