using PocketMC.Infrastructure.Storage;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace PocketMC.Infrastructure.Tests.Linux;

/// <summary>
/// TDD tests for TarGz extraction and Unix execute-bit toggling.
/// </summary>
public class TarGzExtractionTests
{
    private static string CreateTarGzWithExecutable(string dir)
    {
        string archivePath = Path.Combine(dir, "test.tar.gz");
        string binFile = Path.Combine(dir, "test-bin");
        File.WriteAllText(binFile, "#!/bin/sh\necho hello\n");

        using var gz = new FileStream(archivePath, FileMode.Create);
        using var gzip = new GZipStream(gz, CompressionMode.Compress);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
        tar.WriteEntry(binFile, "test-bin");

        File.Delete(binFile);
        return archivePath;
    }

    [Fact]
    public async Task ExtractTarGzAsync_ExtractsFilesSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pocketmc-tartest-{Guid.NewGuid():N}");
        string extractDir = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(extractDir);

        try
        {
            string archivePath = CreateTarGzWithExecutable(tempDir);
            var extractor = new TarGzExtractor();
            await extractor.ExtractAsync(archivePath, extractDir);

            Assert.True(File.Exists(Path.Combine(extractDir, "test-bin")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [SkippableFact]
    public async Task SetExecuteBit_OnLinux_FileBecomesExecutable()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        string tempFile = Path.Combine(Path.GetTempPath(), $"pocketmc-exectest-{Guid.NewGuid():N}");
        File.WriteAllText(tempFile, "#!/bin/sh\necho hi");

        try
        {
            TarGzExtractor.SetExecutable(tempFile);
#pragma warning disable CA1416
            var mode = File.GetUnixFileMode(tempFile);
#pragma warning restore CA1416
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "File should have UserExecute bit set");
            Assert.True(mode.HasFlag(UnixFileMode.UserRead), "File should have UserRead bit set");
            Assert.True(mode.HasFlag(UnixFileMode.UserWrite), "File should have UserWrite bit set");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [SkippableFact]
    public async Task ExtractTarGzAsync_OnLinux_SetsExecuteBitOnExtractedFiles()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        string tempDir = Path.Combine(Path.GetTempPath(), $"pocketmc-tartest-{Guid.NewGuid():N}");
        string extractDir = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(extractDir);

        try
        {
            string archivePath = CreateTarGzWithExecutable(tempDir);
            var extractor = new TarGzExtractor();
            await extractor.ExtractAsync(archivePath, extractDir, setExecutable: true);

            string extracted = Path.Combine(extractDir, "test-bin");
            Assert.True(File.Exists(extracted));

#pragma warning disable CA1416
            var mode = File.GetUnixFileMode(extracted);
#pragma warning restore CA1416
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
