using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Console;

public enum ConsoleSessionLogKind
{
    None,
    CurrentSession,
    LastSession,
    LegacySession
}

public sealed record ConsoleLogReadResult(
    IReadOnlyList<string> Lines,
    string? SourcePath,
    ConsoleSessionLogKind Kind,
    bool IsLive);

public sealed class ConsoleLogHistoryService
{
    public const string CurrentSessionLogName = PocketMC.Domain.Models.LogConstants.CurrentSessionLogName;
    public const string LastSessionLogName = PocketMC.Domain.Models.LogConstants.LastSessionLogName;
    public const string LegacySessionLogName = PocketMC.Domain.Models.LogConstants.LegacySessionLogName;

    private readonly ILogger<ConsoleLogHistoryService> _logger;

    public ConsoleLogHistoryService(ILogger<ConsoleLogHistoryService> logger)
    {
        _logger = logger;
    }

    public string PrepareNewSessionLog(string instancePath, DateTime? timestampUtc = null)
    {
        string logsDir = GetLogsDirectory(instancePath);
        Directory.CreateDirectory(logsDir);

        string currentPath = GetCurrentSessionLogPath(instancePath);
        if (File.Exists(currentPath) && new FileInfo(currentPath).Length > 0)
        {
            RotateCurrentSessionLog(instancePath, timestampUtc ?? DateTime.UtcNow);
        }

        using (new FileStream(currentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
        }

        return currentPath;
    }

    public StreamWriter OpenCurrentSessionWriter(string instancePath)
    {
        string currentPath = PrepareNewSessionLog(instancePath);
        var stream = new FileStream(currentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    }

    public async Task<ConsoleLogReadResult> LoadSessionTailAsync(
        string instancePath,
        int maxLines,
        bool preferCurrentSession,
        bool isLive = false,
        CancellationToken cancellationToken = default)
    {
        maxLines = Math.Max(1, maxLines);
        (string? path, ConsoleSessionLogKind kind) = ResolveSessionLog(instancePath, preferCurrentSession);
        if (path == null)
        {
            return new ConsoleLogReadResult(Array.Empty<string>(), null, ConsoleSessionLogKind.None, false);
        }

        try
        {
            IReadOnlyList<string> lines = await ReadTailLinesAsync(path, maxLines, cancellationToken);
            return new ConsoleLogReadResult(lines, path, kind, isLive && kind == ConsoleSessionLogKind.CurrentSession);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to read console session log at {Path}.", path);
            return new ConsoleLogReadResult(Array.Empty<string>(), path, kind, false);
        }
    }

    public string GetCurrentSessionLogPath(string instancePath)
        => Path.Combine(GetLogsDirectory(instancePath), CurrentSessionLogName);

    public string GetLastSessionLogPath(string instancePath)
        => Path.Combine(GetLogsDirectory(instancePath), LastSessionLogName);

    public string GetLegacySessionLogPath(string instancePath)
        => Path.Combine(GetLogsDirectory(instancePath), LegacySessionLogName);

    public string GetLogsDirectory(string instancePath)
        => Path.Combine(instancePath, "logs");

    public string? GetSessionLogPath(string instancePath, bool preferCurrentSession)
        => ResolveSessionLog(instancePath, preferCurrentSession).Path;

    private void RotateCurrentSessionLog(string instancePath, DateTime timestampUtc)
    {
        string currentPath = GetCurrentSessionLogPath(instancePath);
        string lastPath = GetLastSessionLogPath(instancePath);
        string archivePath = GetArchiveSessionLogPath(instancePath, timestampUtc);

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        File.Copy(currentPath, lastPath, overwrite: true);
        File.Copy(currentPath, archivePath, overwrite: false);
        File.Delete(currentPath);
    }

    private string GetArchiveSessionLogPath(string instancePath, DateTime timestampUtc)
    {
        string sessionsDir = Path.Combine(GetLogsDirectory(instancePath), "sessions");
        string baseName = $"pocketmc-session-{timestampUtc:yyyyMMdd-HHmmss}";
        string archivePath = Path.Combine(sessionsDir, $"{baseName}.log");
        int suffix = 1;

        while (File.Exists(archivePath))
        {
            archivePath = Path.Combine(sessionsDir, $"{baseName}-{suffix}.log");
            suffix++;
        }

        return archivePath;
    }

    private (string? Path, ConsoleSessionLogKind Kind) ResolveSessionLog(string instancePath, bool preferCurrentSession)
    {
        string currentPath = GetCurrentSessionLogPath(instancePath);
        string lastPath = GetLastSessionLogPath(instancePath);
        string legacyPath = GetLegacySessionLogPath(instancePath);

        if (preferCurrentSession && HasContent(currentPath))
        {
            return (currentPath, ConsoleSessionLogKind.CurrentSession);
        }

        if (HasContent(lastPath))
        {
            return (lastPath, ConsoleSessionLogKind.LastSession);
        }

        if (!preferCurrentSession && HasContent(currentPath))
        {
            return (currentPath, ConsoleSessionLogKind.CurrentSession);
        }

        return HasContent(legacyPath)
            ? (legacyPath, ConsoleSessionLogKind.LegacySession)
            : (null, ConsoleSessionLogKind.None);
    }

    private static bool HasContent(string path)
        => File.Exists(path) && new FileInfo(path).Length > 0;

    private static async Task<IReadOnlyList<string>> ReadTailLinesAsync(
        string path,
        int maxLines,
        CancellationToken cancellationToken)
    {
        maxLines = Math.Max(1, maxLines);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);

        long fileLength = stream.Length;
        if (fileLength == 0)
        {
            return Array.Empty<string>();
        }

        // If the file is small (e.g. < 64KB), read it sequentially
        const int InitialBufferSize = 65536;
        if (fileLength <= InitialBufferSize)
        {
            return await ReadAllLinesSequentialAsync(stream, maxLines, cancellationToken);
        }

        var lines = new List<string>();
        long offset = InitialBufferSize;

        while (offset < fileLength && lines.Count < maxLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long seekPosition = fileLength - offset;
            if (seekPosition < 0)
            {
                seekPosition = 0;
            }

            stream.Position = seekPosition;

            // Read from seek position to the end of the file
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
            {
                string block = await reader.ReadToEndAsync(cancellationToken);

                // Split by newline and count lines
                // Note: we might have a partial line at the beginning of the block since we seeked arbitrarily
                var blockLines = block.Split('\n');

                // If we didn't seek to the beginning of the file, the first line is likely partial, so skip it
                int startIdx = (seekPosition > 0) ? 1 : 0;

                lines.Clear();
                for (int i = startIdx; i < blockLines.Length; i++)
                {
                    string trimmed = blockLines[i].TrimEnd('\r');
                    // Skip the last trailing empty line if it ends with a newline
                    if (i == blockLines.Length - 1 && string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }
                    lines.Add(trimmed);
                }
            }

            if (lines.Count >= maxLines || seekPosition == 0)
            {
                break;
            }

            // Increase offset exponentially to locate lines faster in large files
            offset *= 2;
        }

        // Return the last maxLines
        if (lines.Count > maxLines)
        {
            return lines.GetRange(lines.Count - maxLines, maxLines);
        }

        return lines;
    }

    private static async Task<IReadOnlyList<string>> ReadAllLinesSequentialAsync(
        FileStream stream,
        int maxLines,
        CancellationToken cancellationToken)
    {
        var lines = new Queue<string>(maxLines);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }
            if (lines.Count == maxLines)
            {
                lines.Dequeue();
            }
            lines.Enqueue(line);
        }
        return lines.ToArray();
    }
}
