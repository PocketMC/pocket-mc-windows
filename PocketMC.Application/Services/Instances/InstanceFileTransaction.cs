using System.Text.Json;
using PocketMC.Domain.Security;
using PocketMC.Domain.Storage;

namespace PocketMC.Application.Services.Instances;

/// <summary>Same-volume promotion with durable rollback for interrupted multi-root changes.</summary>
public static class InstanceFileTransaction
{
    private const string JournalName = ".pocketmc-file-transaction.json";
    public sealed record Replacement(string? StagedPath, string RelativePath);
    public sealed record Entry(string RelativePath, string BackupRelativePath, bool Existed);
    public sealed record Journal(List<Entry> Entries, bool Committed = false);

    public static bool Recover(string serverDir)
    {
        string journalPath = Path.Combine(serverDir, JournalName);
        if (!File.Exists(journalPath)) return false;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath))
            ?? throw new InvalidDataException("The file recovery journal is unreadable. Original files have been preserved.");
        if (!journal.Committed)
        {
            foreach (var entry in journal.Entries.AsEnumerable().Reverse())
            {
                string target = PathSafety.ValidateContainedPath(serverDir, entry.RelativePath) ?? throw new InvalidDataException("Invalid recovery target.");
                string backup = PathSafety.ValidateContainedPath(serverDir, entry.BackupRelativePath) ?? throw new InvalidDataException("Invalid recovery source.");
                if (!entry.BackupRelativePath.StartsWith(".pocketmc-rollback-", StringComparison.Ordinal)) throw new InvalidDataException("Invalid rollback path.");
                if (Exists(backup)) { Delete(target); Move(backup, target); }
                else if (!entry.Existed) Delete(target);
            }
        }
        foreach (var entry in journal.Entries)
        {
            string backup = PathSafety.ValidateContainedPath(serverDir, entry.BackupRelativePath) ?? throw new InvalidDataException("Invalid rollback path.");
            Delete(backup);
            string parent = Path.GetDirectoryName(backup)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
        File.Delete(journalPath);
        return true;
    }

    public static void Commit(string serverDir, IReadOnlyList<Replacement> replacements, CancellationToken cancellationToken = default)
    {
        Recover(serverDir);
        string rollback = $".pocketmc-rollback-{Guid.NewGuid():N}";
        var journal = new Journal(new());
        string journalPath = Path.Combine(serverDir, JournalName);
        try
        {
            foreach (var replacement in replacements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = PathSafety.ValidateContainedPath(serverDir, replacement.RelativePath) ?? throw new InvalidDataException("Unsafe transaction target.");
                if (replacement.StagedPath != null)
                {
                    PathSafety.RequireNoLinks(replacement.StagedPath);
                    if (!Exists(replacement.StagedPath)) throw new FileNotFoundException("Staged replacement is missing.", replacement.StagedPath);
                }
                string relativeBackup = $"{rollback}/{journal.Entries.Count}";
                string backup = PathSafety.ValidateContainedPath(serverDir, relativeBackup)!;
                journal.Entries.Add(new(replacement.RelativePath, relativeBackup, Exists(target)));
                FileUtils.AtomicWriteAllText(journalPath, JsonSerializer.Serialize(journal));
                if (Exists(target)) Move(target, backup);
                if (replacement.StagedPath != null) Move(replacement.StagedPath, target);
            }
            FileUtils.AtomicWriteAllText(journalPath, JsonSerializer.Serialize(journal with { Committed = true }));
        }
        catch
        {
            Recover(serverDir);
            throw;
        }
        // A cleanup error leaves a committed journal, never rolls back successfully promoted data.
        Recover(serverDir);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static void Move(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
    }
    private static void Delete(string path)
    {
        PathSafety.RequireNoLinks(path);
        if (Directory.Exists(path)) Directory.Delete(path, true); else if (File.Exists(path)) File.Delete(path);
    }
}
