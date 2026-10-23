using System.IO.Compression;
using PocketMC.Domain.Security;

namespace PocketMC.Domain.Storage;

public sealed record ArchiveLimits(long MaxBytes = 64L * 1024 * 1024 * 1024, int MaxEntries = 500_000, int MaxDepth = 64);

public static class SafeZipExtractor
{
    public static Task ExtractAsync(string zipPath, string extractPath, Action<long, long>? onProgress = null,
        CancellationToken cancellationToken = default, ArchiveLimits? limits = null)
    {
        return Task.Run(() =>
        {
            limits ??= new ArchiveLimits();
            PathSafety.RequireNoLinks(extractPath);
            Directory.CreateDirectory(extractPath);
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count > limits.MaxEntries) throw new InvalidDataException("Archive contains too many entries.");
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? target = PathSafety.ValidateContainedPath(extractPath, entry.FullName);
                if (target == null || !targets.Add(target.TrimEnd(Path.DirectorySeparatorChar)) ||
                    entry.FullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Length > limits.MaxDepth ||
                    (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException($"Unsafe or duplicate archive entry: {entry.FullName}");
                total = checked(total + entry.Length);
                if (total > limits.MaxBytes) throw new InvalidDataException("Archive exceeds the extraction size limit.");
            }
            string? driveRoot = Path.GetPathRoot(Path.GetFullPath(extractPath));
            if (driveRoot != null && new DriveInfo(driveRoot).AvailableFreeSpace < total + 64L * 1024 * 1024)
                throw new IOException("Not enough free space to extract this archive safely.");
            long written = 0, count = 0;
            byte[] buffer = new byte[81920];
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = PathSafety.ValidateContainedPath(extractPath, entry.FullName)
                    ?? throw new InvalidDataException("Archive destination changed during extraction.");
                if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    PathSafety.RequireNoLinks(target);
                    using var input = entry.Open();
                    using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    long entryWritten = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        written = checked(written + read);
                        entryWritten += read;
                        if (written > limits.MaxBytes || entryWritten > entry.Length) throw new InvalidDataException("Archive expanded beyond its declared size.");
                        output.Write(buffer, 0, read);
                    }
                    if (entryWritten != entry.Length) throw new InvalidDataException("Truncated archive entry.");
                }
                onProgress?.Invoke(++count, archive.Entries.Count);
            }
        }, cancellationToken);
    }
}
