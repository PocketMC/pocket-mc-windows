using System.IO;
using System.Text.Json;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Instances.Updates;

public sealed class InstanceRollbackService
{
    private readonly InstanceUpdateJournalStore _journalStore;

    public InstanceRollbackService(InstanceUpdateJournalStore journalStore)
    {
        _journalStore = journalStore;
    }

    private static string GetRollbackDirectory(string serverDir)
    {
        return serverDir.TrimEnd('/', '\\') + "-rollback";
    }

    public async Task<string> CreateSnapshotAsync(
        InstanceUpdatePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string rollbackDirectory = GetRollbackDirectory(plan.ServerDir);
        if (Directory.Exists(rollbackDirectory))
        {
            await FileUtils.CleanDirectoryAsync(rollbackDirectory, cancellationToken);
            Directory.Delete(rollbackDirectory, recursive: true);
        }

        Directory.CreateDirectory(rollbackDirectory);
        string serverRoot = Path.GetFullPath(plan.ServerDir);

        await FileUtils.CopyDirectoryAsync(serverRoot, rollbackDirectory);

        return rollbackDirectory;
    }

    public async Task RollbackAsync(
        string serverDir,
        bool restoreWorldBackup = false,
        CancellationToken cancellationToken = default)
    {
        string rollbackDirectory = GetRollbackDirectory(serverDir);
        if (!Directory.Exists(rollbackDirectory))
        {
            return;
        }

        string serverRoot = Path.GetFullPath(serverDir);

        if (Directory.Exists(serverRoot))
        {
            string tempDeleteDir = serverRoot + "_todelete_" + Guid.NewGuid().ToString("N");
            Directory.Move(serverRoot, tempDeleteDir);

            Directory.Move(rollbackDirectory, serverRoot);

            try
            {
                await FileUtils.CleanDirectoryAsync(tempDeleteDir, cancellationToken);
                Directory.Delete(tempDeleteDir, recursive: true);
            }
            catch
            {
                // Ignore background cleanup errors
            }
        }
        else
        {
            Directory.Move(rollbackDirectory, serverRoot);
        }
    }

    public bool HasRollbackBackup(string serverDir)
    {
        return Directory.Exists(GetRollbackDirectory(serverDir));
    }

    public async Task DeleteRollbackBackupAsync(string serverDir, CancellationToken cancellationToken = default)
    {
        string rollbackDirectory = GetRollbackDirectory(serverDir);
        if (Directory.Exists(rollbackDirectory))
        {
            await FileUtils.CleanDirectoryAsync(rollbackDirectory, cancellationToken);
            Directory.Delete(rollbackDirectory, recursive: true);
        }
    }
}
