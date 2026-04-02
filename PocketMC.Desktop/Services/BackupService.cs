using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Handles world backup creation with safe save-lock handshake for live servers,
    /// locked-file tolerance, and automatic retention pruning.
    /// </summary>
    public class BackupService
    {
        // Files the JVM holds exclusively — always skip these
        private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "session.lock"
        };

        /// <summary>
        /// Creates a timestamped world backup ZIP, using safe I/O synchronization
        /// if the server is currently running. Tolerates locked files gracefully.
        /// </summary>
        public async Task RunBackupAsync(InstanceMetadata metadata, string serverDir, Action<string>? onProgress = null)
        {
            var worldDir = Path.Combine(serverDir, "world");
            if (!Directory.Exists(worldDir))
            {
                throw new DirectoryNotFoundException("No world folder found — nothing to back up.");
            }

            var backupDir = Path.Combine(serverDir, "backups");
            Directory.CreateDirectory(backupDir);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            string zipPath = Path.Combine(backupDir, $"world-{timestamp}.zip");

            bool isRunning = ServerProcessManager.IsRunning(metadata.Id);
            var process = ServerProcessManager.GetProcess(metadata.Id);
            var skippedFiles = new List<string>();

            try
            {
                if (isRunning && process != null)
                {
                    // Safe handshake: disable auto-save, force flush, wait for completion
                    onProgress?.Invoke("Disabling auto-save...");
                    await process.WriteInputAsync("save-off");
                    await Task.Delay(500);

                    onProgress?.Invoke("Flushing world to disk...");
                    await process.WriteInputAsync("save-all");

                    onProgress?.Invoke("Waiting for save to complete...");
                    bool saved = await process.WaitForConsoleOutputAsync("Saved the game", TimeSpan.FromSeconds(30));

                    if (!saved)
                    {
                        await process.WriteInputAsync("save-on");
                        throw new TimeoutException("Server did not confirm save within 30 seconds. Backup aborted.");
                    }
                }

                // Compress world folder with locked-file tolerance
                onProgress?.Invoke("Compressing world...");
                await Task.Run(() => CreateZipWithLockedFileSkip(worldDir, zipPath, skippedFiles));

                // Verify we actually wrote something meaningful
                var zipInfo = new FileInfo(zipPath);
                if (!zipInfo.Exists || zipInfo.Length == 0)
                {
                    try { File.Delete(zipPath); } catch { }
                    throw new IOException("Backup produced an empty ZIP file.");
                }

                if (skippedFiles.Count > 0)
                    onProgress?.Invoke($"Backup complete! ({skippedFiles.Count} locked file(s) skipped)");
                else
                    onProgress?.Invoke("Backup complete!");
            }
            catch
            {
                // Clean up partial/failed ZIP so 0-byte ghosts don't appear in the list
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                throw;
            }
            finally
            {
                // Always re-enable auto-save if server was running
                if (isRunning && process != null)
                {
                    try { await process.WriteInputAsync("save-on"); }
                    catch { /* best effort */ }
                }
            }

            // Update metadata
            metadata.LastBackupTime = DateTime.UtcNow;
            SaveMetadata(metadata, serverDir);

            // Prune old backups
            PruneOldBackups(backupDir, metadata.MaxBackupsToKeep);
        }

        /// <summary>
        /// Builds a ZIP manually, opening each file with FileShare.ReadWrite
        /// so JVM-locked region files can still be read, and skipping any
        /// file that is exclusively locked (like session.lock).
        /// </summary>
        private void CreateZipWithLockedFileSkip(string sourceDir, string zipPath, List<string> skippedFiles)
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            foreach (var filePath in allFiles)
            {
                string relativePath = Path.GetRelativePath(sourceDir, filePath);
                string fileName = Path.GetFileName(filePath);

                // Always skip known locked files
                if (SkipFiles.Contains(fileName))
                {
                    skippedFiles.Add(relativePath);
                    continue;
                }

                try
                {
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    // FileShare.ReadWrite allows reading files the JVM has open
                    using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fileStream.CopyTo(entryStream);
                }
                catch (IOException)
                {
                    // File is exclusively locked — skip it rather than fail the whole backup
                    skippedFiles.Add(relativePath);
                }
                catch (UnauthorizedAccessException)
                {
                    skippedFiles.Add(relativePath);
                }
            }
        }

        /// <summary>
        /// Restores a backup by replacing the world folder with the backup contents.
        /// Server MUST be stopped before calling this.
        /// </summary>
        public async Task RestoreBackupAsync(string backupZipPath, string serverDir, Action<string>? onProgress = null)
        {
            var worldDir = Path.Combine(serverDir, "world");

            onProgress?.Invoke("Removing current world...");
            if (Directory.Exists(worldDir))
            {
                await Utils.FileUtils.CleanDirectoryAsync(worldDir);
            }

            onProgress?.Invoke("Extracting backup...");
            await Task.Run(() => ZipFile.ExtractToDirectory(backupZipPath, worldDir));

            onProgress?.Invoke("World restored successfully!");
        }

        /// <summary>
        /// Deletes oldest backups that exceed the retention limit.
        /// </summary>
        private void PruneOldBackups(string backupDirectory, int maxToKeep)
        {
            var files = new DirectoryInfo(backupDirectory)
                .GetFiles("world-*.zip")
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            for (int i = maxToKeep; i < files.Count; i++)
            {
                try { files[i].Delete(); }
                catch { /* best effort */ }
            }
        }

        private void SaveMetadata(InstanceMetadata metadata, string serverDir)
        {
            var metaFile = Path.Combine(serverDir, ".pocket-mc.json");
            var json = System.Text.Json.JsonSerializer.Serialize(metadata,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaFile, json);
        }
    }
}

