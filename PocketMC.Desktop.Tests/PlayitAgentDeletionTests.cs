using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests
{
    public sealed class PlayitAgentDeletionTests
    {
        [Fact]
        public async Task DeleteAgentBinaryAsync_WhenBinaryMissing_ReturnsTrue()
        {
            using var workspace = new PortReliabilityTestWorkspace();
            var harness = workspace.CreatePlayitAgentHarness();

            bool result = await harness.Service.DeleteAgentBinaryAsync();
            Assert.True(result);
        }

        [Fact]
        public async Task DeleteAgentBinaryAsync_WhenPartialExists_DeletesPartialAndReturnsTrue()
        {
            using var workspace = new PortReliabilityTestWorkspace();
            var harness = workspace.CreatePlayitAgentHarness();
            string exePath = workspace.AppState.GetPlayitExecutablePath();
            string partialPath = exePath + ".partial";

            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            await File.WriteAllTextAsync(partialPath, "stub partial");

            Assert.True(File.Exists(partialPath));
            bool result = await harness.Service.DeleteAgentBinaryAsync();
            Assert.True(result);
            Assert.False(File.Exists(partialPath));
        }

        [Fact]
        public async Task DeleteAgentBinaryAsync_WhenOrphanProcessExists_KillsAndDeletesBinary()
        {
            using var workspace = new PortReliabilityTestWorkspace();
            var harness = workspace.CreatePlayitAgentHarness();
            string exePath = workspace.AppState.GetPlayitExecutablePath();
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);

            // Copy a standard system executable (e.g. cmd.exe) to serve as playit.exe
            string systemCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            File.Copy(systemCmd, exePath, overwrite: true);

            // Start it as an orphan process
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "/k", // keeps cmd.exe open/running
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            Assert.NotNull(process);
            try
            {
                // Give it a moment to start
                await Task.Delay(150);

                Assert.True(File.Exists(exePath));
                // Check that the process is running
                Assert.False(process.HasExited);

                // Now, run the deletion logic
                bool result = await harness.Service.DeleteAgentBinaryAsync();
                Assert.True(result);

                // The process should have been terminated and file deleted
                Assert.True(process.HasExited);
                Assert.False(File.Exists(exePath));
            }
            finally
            {
                if (process != null && !process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
        }

        [Fact]
        public async Task DeleteAgentBinaryAsync_WhenFileIsLockedAndCannotBeDeleted_FallbacksToRename()
        {
            using var workspace = new PortReliabilityTestWorkspace();
            var harness = workspace.CreatePlayitAgentHarness();
            string exePath = workspace.AppState.GetPlayitExecutablePath();
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);

            // Create a file and lock it (keep file stream open)
            await File.WriteAllTextAsync(exePath, "original content");

            // Open it with FileShare.Read | FileShare.Delete to prevent normal deletion but allow renaming
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                bool result = await harness.Service.DeleteAgentBinaryAsync();
                Assert.True(result);

                // The original file path should no longer exist because it was renamed
                Assert.False(File.Exists(exePath));

                // Let's verify that a pending delete file was created in the same directory
                string dir = Path.GetDirectoryName(exePath)!;
                string[] pendingFiles = Directory.GetFiles(dir, "*.delete-pending.exe");
                Assert.Single(pendingFiles);
            }

            // Clean up pending files by calling CleanPendingDeletes after lock is released
            harness.Service.CleanPendingDeletes();
            string dir2 = Path.GetDirectoryName(exePath)!;
            Assert.Empty(Directory.GetFiles(dir2, "*.delete-pending.exe"));
        }
    }
}


