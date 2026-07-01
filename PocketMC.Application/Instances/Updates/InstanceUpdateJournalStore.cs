using System.IO;
using System.Text.Json;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Application.Instances.Updates;

public sealed class InstanceUpdateJournalStore
{
    public const string UpdatesRootName = ".pocketmc-updates";
    private const string JournalsDirectoryName = "journals";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task SaveAsync(InstanceUpdateJournal journal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        journal.UpdatedAtUtc = DateTime.UtcNow;

        string path = GetJournalPath(journal.ServerDir, journal.OperationId);
        string json = JsonSerializer.Serialize(journal, JsonOptions);
        await FileUtils.AtomicWriteAllTextAsync(path, json, cancellationToken: cancellationToken);
    }

    public async Task<InstanceUpdateJournal?> LoadAsync(
        string serverDir,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        string path = GetJournalPath(serverDir, operationId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        return await JsonSerializer.DeserializeAsync<InstanceUpdateJournal>(stream, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<InstanceUpdateJournal>> GetIncompleteJournalsAsync(
        string serverDir,
        CancellationToken cancellationToken = default)
    {
        string journalDirectory = GetJournalDirectory(serverDir);
        if (!Directory.Exists(journalDirectory))
        {
            return Array.Empty<InstanceUpdateJournal>();
        }

        var results = new List<InstanceUpdateJournal>();
        foreach (string file in Directory.EnumerateFiles(journalDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
                InstanceUpdateJournal? journal = await JsonSerializer.DeserializeAsync<InstanceUpdateJournal>(stream, cancellationToken: cancellationToken);
                if (journal != null && IsIncomplete(journal.State))
                {
                    results.Add(journal);
                }
            }
            catch
            {
                // Ignore corrupt journal files; callers can still inspect the directory manually.
            }
        }

        return results
            .OrderByDescending(journal => journal.UpdatedAtUtc)
            .ToArray();
    }

    public async Task<InstanceUpdateJournal?> GetLatestRecoverableJournalAsync(
        string serverDir,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InstanceUpdateJournal> journals = await GetIncompleteJournalsAsync(serverDir, cancellationToken);
        return journals.FirstOrDefault(journal => !string.IsNullOrWhiteSpace(journal.SnapshotDirectory));
    }

    public string GetOperationRoot(string serverDir, string childDirectoryName, Guid operationId)
    {
        string updatesRoot = GetUpdatesRoot(serverDir);
        string? childRoot = PathSafety.ValidateContainedPath(updatesRoot, childDirectoryName)
            ?? throw new InvalidOperationException($"Update child directory '{childDirectoryName}' is invalid.");
        return PathSafety.ValidateContainedPath(childRoot, operationId.ToString("N"))
            ?? throw new InvalidOperationException($"Update operation id '{operationId}' is invalid.");
    }

    public string GetJournalDirectory(string serverDir)
    {
        string updatesRoot = GetUpdatesRoot(serverDir);
        return PathSafety.ValidateContainedPath(updatesRoot, JournalsDirectoryName)
            ?? throw new InvalidOperationException("Journal directory path is invalid.");
    }

    private string GetJournalPath(string serverDir, Guid operationId)
    {
        string journalDirectory = GetJournalDirectory(serverDir);
        return PathSafety.ValidateContainedPath(journalDirectory, $"{operationId:N}.json")
            ?? throw new InvalidOperationException("Journal file path is invalid.");
    }

    private static string GetUpdatesRoot(string serverDir)
    {
        string root = Path.GetFullPath(serverDir);
        return PathSafety.ValidateContainedPath(root, UpdatesRootName)
            ?? throw new InvalidOperationException("Updates directory path is invalid.");
    }

    private static bool IsIncomplete(InstanceUpdateJournalState state)
    {
        return state is not InstanceUpdateJournalState.Completed
            and not InstanceUpdateJournalState.RolledBack;
    }
}
