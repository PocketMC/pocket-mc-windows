using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Infrastructure.Storage;

/// <summary>
/// Extracts .tar.gz archives using <see cref="System.Formats.Tar.TarReader"/> (.NET 7+).
/// Optionally sets UserRead | UserWrite | UserExecute on each extracted file on Unix.
/// </summary>
public sealed class TarGzExtractor
{
    /// <summary>
    /// Extracts a .tar.gz archive at <paramref name="archivePath"/> into <paramref name="extractPath"/>.
    /// </summary>
    /// <param name="archivePath">Path to the .tar.gz file.</param>
    /// <param name="extractPath">Directory to extract into (created if absent).</param>
    /// <param name="setExecutable">
    /// When <c>true</c> and running on a Unix platform, each extracted file receives
    /// <see cref="UnixFileMode.UserRead"/> | <see cref="UnixFileMode.UserWrite"/> | <see cref="UnixFileMode.UserExecute"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExtractAsync(
        string archivePath,
        string extractPath,
        bool setExecutable = false,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(extractPath);

        await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream, leaveOpen: false);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync(copyData: true, cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.EntryType is TarEntryType.Directory)
            {
                string dirPath = Path.Combine(extractPath, SanitizeEntryName(entry.Name));
                Directory.CreateDirectory(dirPath);
                continue;
            }

            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                string filePath = Path.GetFullPath(Path.Combine(extractPath, SanitizeEntryName(entry.Name)));

                // Guard against Zip-Slip path traversal
                if (!filePath.StartsWith(Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Tar entry path traversal detected: {entry.Name}");

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                if (entry.DataStream is not null)
                {
                    await using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                    await entry.DataStream.CopyToAsync(outputStream, cancellationToken);
                }

                if (setExecutable && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
                {
                    SetExecutable(filePath);
                }
            }
        }
    }

    /// <summary>
    /// Sets Unix file mode to UserRead | UserWrite | UserExecute (chmod 700) on <paramref name="filePath"/>.
    /// This is a no-op on Windows.
    /// </summary>
    public static void SetExecutable(string filePath)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        File.SetUnixFileMode(filePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Removes leading slashes and normalises path separators to prevent traversal attacks.
    /// </summary>
    private static string SanitizeEntryName(string name)
        => name.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
}
