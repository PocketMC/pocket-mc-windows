using System.IO.Compression;
using PocketMC.Domain.Security;
using PocketMC.Domain.Storage;

namespace PocketMC.Domain.Tests;

public sealed class ArchiveBoundaryRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC-Archive-" + Guid.NewGuid().ToString("N"));
    public ArchiveBoundaryRegressionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData("../sibling/file")]
    [InlineData("dir/CON.txt")]
    [InlineData("dir/file:stream")]
    [InlineData("dir/trailing. ")]
    public void UnsafePaths_AreRejected(string path) => Assert.Null(PathSafety.ValidateContainedPath(_root, path));

    [Fact]
    public async Task ExistingLink_CannotRedirectExtractionOutsideTheRoot()
    {
        string output = Path.Combine(_root, "output"), outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(output); Directory.CreateDirectory(outside);
        string link = Path.Combine(output, "redirect");
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c mklink /J \"{link}\" \"{outside}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process!.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside);
        try
        {
            string archive = Path.Combine(_root, "link.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("redirect/escape.txt");
            await Assert.ThrowsAsync<InvalidDataException>(() => SafeZipExtractor.ExtractAsync(archive, output));
            Assert.False(File.Exists(Path.Combine(outside, "escape.txt")));
        }
        finally
        {
            // Remove the junction before recursive fixture cleanup can delete its target.
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task ArchiveBudget_RejectsBeforeExtractingContent()
    {
        string zipPath = Path.Combine(_root, "input.zip"), output = Path.Combine(_root, "output");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open())) writer.Write(new string('x', 1024));
        await Assert.ThrowsAsync<InvalidDataException>(() => SafeZipExtractor.ExtractAsync(zipPath, output, limits: new ArchiveLimits(MaxBytes: 10)));
        Assert.False(File.Exists(Path.Combine(output, "payload.txt")));
    }

    [Fact]
    public async Task DuplicateTargets_AreRejectedBeforeFirstWrite()
    {
        string zipPath = Path.Combine(_root, "input.zip"), output = Path.Combine(_root, "output");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { zip.CreateEntry("payload.txt"); zip.CreateEntry("PAYLOAD.TXT"); }
        await Assert.ThrowsAsync<InvalidDataException>(() => SafeZipExtractor.ExtractAsync(zipPath, output));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }
}
