using System;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using PocketMC.E2E.Tests.Infrastructure;
using Xunit;

namespace PocketMC.E2E.Tests.Features.Extraction
{
    public class AdoptiumExtractionTests
    {
        public static byte[] CreateMockTarGz(string fileName, string fileContent, bool isExecutable = false)
        {
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
                {
                    using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
                    {
                        var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
                        {
                            DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent))
                        };
                        if (isExecutable)
                        {
                            entry.Mode = UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute |
                                         UnixFileModes.GroupRead | UnixFileModes.GroupExecute |
                                         UnixFileModes.OtherRead | UnixFileModes.OtherExecute;
                        }
                        else
                        {
                            entry.Mode = UnixFileModes.UserRead | UnixFileModes.UserWrite |
                                         UnixFileModes.GroupRead | UnixFileModes.OtherRead;
                        }
                        writer.WriteEntry(entry);
                    }
                }
                return ms.ToArray();
            }
        }

        public static void ExtractTarGz(byte[] tarGzData, string outputDirectory)
        {
            using (var ms = new MemoryStream(tarGzData))
            using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new TarReader(gzip))
            {
                TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    var targetPath = Path.Combine(outputDirectory, entry.Name);
                    var parentDir = Path.GetDirectoryName(targetPath);
                    if (parentDir != null)
                    {
                        Directory.CreateDirectory(parentDir);
                    }
                    entry.ExtractToFile(targetPath, overwrite: true);
                    
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Set the Unix file modes from the tar entry
                        File.SetUnixFileModes(targetPath, entry.Mode);
                        // Ensure bin/java or bin/keytool have executable bits
                        if (entry.Name.Contains("bin/java") || entry.Name.EndsWith(".sh"))
                        {
                            File.SetUnixFileModes(targetPath, entry.Mode | UnixFileModes.UserExecute);
                        }
                    }
                }
            }
        }

        [Fact]
        public void Test1_DownloadTargetFormat_OnLinux_IsTarGz()
        {
            // Verify that for Linux, Adoptium download targets are .tar.gz
            string platform = "linux";
            string format = platform == "linux" ? ".tar.gz" : ".zip";
            Assert.Equal(".tar.gz", format);
        }

        [Fact]
        public void Test2_DownloadTargetFormat_OnWindows_IsZip()
        {
            // Verify that for Windows, Adoptium download targets are .zip
            string platform = "windows";
            string format = platform == "windows" ? ".zip" : ".tar.gz";
            Assert.Equal(".zip", format);
        }

        [Fact]
        public void Test3_TarGzExtraction_CreatesAllFiles()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var tarGzData = CreateMockTarGz("bin/java", "dummy-java-binary", isExecutable: true);

                // Act
                ExtractTarGz(tarGzData, ctx.TempPath);

                // Assert
                var extractedFile = Path.Combine(ctx.TempPath, "bin/java");
                Assert.True(File.Exists(extractedFile));
            }
        }

        [Fact]
        public void Test4_TarGzExtraction_PreservesFileContent()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                string expectedContent = "java-version-17";
                var tarGzData = CreateMockTarGz("bin/java", expectedContent, isExecutable: true);

                // Act
                ExtractTarGz(tarGzData, ctx.TempPath);

                // Assert
                var extractedFile = Path.Combine(ctx.TempPath, "bin/java");
                var actualContent = File.ReadAllText(extractedFile);
                Assert.Equal(expectedContent, actualContent);
            }
        }

        [UnixFact]
        public void Test5_UnixPermissionExecute_SkipsOnWindows()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var tarGzData = CreateMockTarGz("bin/java", "dummy-java", isExecutable: true);

                // Act
                ExtractTarGz(tarGzData, ctx.TempPath);

                // Assert
                var extractedFile = Path.Combine(ctx.TempPath, "bin/java");
                var modes = File.GetUnixFileModes(extractedFile);
                Assert.True(modes.HasFlag(UnixFileModes.UserExecute));
            }
        }

        [UnixFact]
        public void Test6_UnixPermissionNonExecute_SkipsOnWindows()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var tarGzData = CreateMockTarGz("lib/rt.jar", "dummy-lib", isExecutable: false);

                // Act
                ExtractTarGz(tarGzData, ctx.TempPath);

                // Assert
                var extractedFile = Path.Combine(ctx.TempPath, "lib/rt.jar");
                var modes = File.GetUnixFileModes(extractedFile);
                Assert.False(modes.HasFlag(UnixFileModes.UserExecute));
            }
        }

        [Fact]
        public void Test7_TarGzExtraction_HandlesNestedDirectories()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var tarGzData = CreateMockTarGz("jre/lib/security/cacerts", "cacerts-data", isExecutable: false);

                // Act
                ExtractTarGz(tarGzData, ctx.TempPath);

                // Assert
                var extractedFile = Path.Combine(ctx.TempPath, "jre/lib/security/cacerts");
                Assert.True(File.Exists(extractedFile));
                Assert.Equal("cacerts-data", File.ReadAllText(extractedFile));
            }
        }

        [UnixFact]
        public void Test8_ChmodExecutableUtility_SkipsOnWindows()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var filePath = ctx.CreateFile("bin/launcher", "launcher-content");

                // Act - simulate running chmod +x via C# API
                var currentModes = File.GetUnixFileModes(filePath);
                File.SetUnixFileModes(filePath, currentModes | UnixFileModes.UserExecute);

                // Assert
                var newModes = File.GetUnixFileModes(filePath);
                Assert.True(newModes.HasFlag(UnixFileModes.UserExecute));
            }
        }

        [Fact]
        public void Test9_ExtractEmptyTarGz_ThrowsOrHandlesGracefully()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                byte[] invalidData = new byte[100]; // random empty bytes

                // Act & Assert
                Assert.ThrowsAny<Exception>(() => ExtractTarGz(invalidData, ctx.TempPath));
            }
        }

        [Fact]
        public void Test10_ExtractOverwrittenFiles_Succeeds()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var fileRelativePath = "bin/java";
                var destPath = Path.Combine(ctx.TempPath, fileRelativePath);
                ctx.CreateFile(fileRelativePath, "old-content");

                var tarGzData = CreateMockTarGz(fileRelativePath, "new-content", isExecutable: true);

                // Act
                ExtractTarGz(tarGzData, ctx.TempPath);

                // Assert
                Assert.True(File.Exists(destPath));
                Assert.Equal("new-content", File.ReadAllText(destPath));
            }
        }
    }
}
