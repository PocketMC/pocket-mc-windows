using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Domain.Storage
{
    /// <summary>
    /// Production-grade recursive file operations that safely handle
    /// cross-drive moves, read-only attributes, and async offloading.
    /// </summary>
    public static class FileUtils
    {
        /// <summary>
        /// Recursively copies a directory tree. Safe across drive boundaries
        /// (unlike Directory.Move which fails cross-volume).
        /// </summary>
        public static async Task CopyDirectoryAsync(string sourceDir, string destDir)
        {
            await Task.Run(() =>
            {
                CopyDirectoryRecursive(sourceDir, destDir);
            });
        }

        private static void CopyDirectoryRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source))
            {
                var destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var destSubDir = Path.Combine(dest, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, destSubDir);
            }
        }

        public static async Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var fileMode = overwrite ? FileMode.Create : FileMode.CreateNew;
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var destinationStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await sourceStream.CopyToAsync(destinationStream);
        }

        public static Task DeleteFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            });
        }

        public static void AtomicWriteAllText(string filePath, string contents, Encoding? encoding = null)
        {
            encoding ??= new UTF8Encoding(false);
            string targetPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(tempPath, contents, encoding);
                ReplaceWithTempFile(tempPath, targetPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        public static async Task AtomicWriteAllTextAsync(
            string filePath,
            string contents,
            Encoding? encoding = null,
            CancellationToken cancellationToken = default)
        {
            encoding ??= new UTF8Encoding(false);
            string targetPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await File.WriteAllTextAsync(tempPath, contents, encoding, cancellationToken);
                ReplaceWithTempFile(tempPath, targetPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        private static void ReplaceWithTempFile(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                string backupPath = $"{targetPath}.{Guid.NewGuid():N}.bak";
                File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
                TryDeleteFile(backupPath);
                return;
            }

            File.Move(tempPath, targetPath);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Best-effort cleanup only; the original write error is more important.
            }
        }

        /// <summary>
        /// Forcefully deletes a directory, stripping read-only attributes first
        /// to avoid UnauthorizedAccessException on protected files.
        /// </summary>
        public static async Task CleanDirectoryAsync(string dirPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(dirPath)) return;

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                StripReadOnly(dirPath);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Delete(dirPath, recursive: true);
            }, cancellationToken);
        }

        private static void StripReadOnly(string dirPath)
        {
            foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }

            foreach (var directory in Directory.GetDirectories(dirPath, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(directory);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(directory, attrs & ~FileAttributes.ReadOnly);
            }

            var rootAttrs = File.GetAttributes(dirPath);
            if ((rootAttrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(dirPath, rootAttrs & ~FileAttributes.ReadOnly);
        }

        /// <summary>
        /// Returns folder size in MB for display purposes.
        /// </summary>
        public static double GetDirectorySizeMb(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return 0;
            long totalBytes = 0;
            foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                try { totalBytes += new FileInfo(file).Length; }
                catch { /* skip locked files */ }
            }
            return Math.Round(totalBytes / (1024.0 * 1024.0), 1);
        }
    }
}
