using System.IO.Compression;
using PocketMC.Desktop.Utils;

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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
