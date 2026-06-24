using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Mods;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Desktop.Features.Instances.Updates;

public sealed class InstanceUpdateApplier
{
    private static readonly string[] BedrockPreservedEntries =
    [
        "worlds",
        "behavior_packs",
        "resource_packs",
        "config",
        "server.properties",
        "eula.txt",
        "permissions.json",
        "allowlist.json"
    ];

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly InstanceRollbackService _rollbackService;
    private readonly AddonMigrationApplier _addonApplier;
    private readonly InstanceUpdateJournalStore _journalStore;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly Func<InstanceMetadata, string, Action<string>?, Task> _backupRunner;
    private readonly ILogger<InstanceUpdateApplier> _logger;
    private readonly InstanceManager? _instanceManager;

    public InstanceUpdateApplier(
        InstanceRollbackService rollbackService,
        AddonMigrationApplier addonApplier,
        InstanceUpdateJournalStore journalStore,
        IServerLifecycleService lifecycleService,
        InstanceManager instanceManager,
        BackupService backupService,
        ILogger<InstanceUpdateApplier> logger)
        : this(
            rollbackService,
            addonApplier,
            journalStore,
            lifecycleService,
            (metadata, serverDir, progress) => backupService.RunBackupAsync(metadata, serverDir, isManualBackup: false, onProgress: progress),
            logger,
            instanceManager)
    {
    }

    public InstanceUpdateApplier(
        InstanceRollbackService rollbackService,
        AddonMigrationApplier addonApplier,
        InstanceUpdateJournalStore journalStore,
        IServerLifecycleService lifecycleService,
        Func<InstanceMetadata, string, Action<string>?, Task> backupRunner,
        ILogger<InstanceUpdateApplier> logger,
        InstanceManager? instanceManager = null)
    {
        _rollbackService = rollbackService;
        _addonApplier = addonApplier;
        _journalStore = journalStore;
        _lifecycleService = lifecycleService;
        _backupRunner = backupRunner;
        _logger = logger;
        _instanceManager = instanceManager;
    }

    public async Task<InstanceUpdateApplyResult> ApplyAsync(
        InstanceUpdatePlan plan,
        InstanceUpdateStagedArtifacts stagedArtifacts,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stagedArtifacts);

        var journal = new InstanceUpdateJournal
        {
            OperationId = plan.OperationId,
            InstanceId = plan.InstanceId,
            ServerDir = Path.GetFullPath(plan.ServerDir),
            StagingDirectory = stagedArtifacts.StagingDirectory,
            State = InstanceUpdateJournalState.Planned
        };

        await _journalStore.SaveAsync(journal, cancellationToken);

        try
        {
            await UpdateJournalAsync(journal, InstanceUpdateJournalState.StoppingServer, cancellationToken);
            if (_lifecycleService.IsRunning(plan.InstanceId))
            {
                onProgress?.Invoke("Stopping server...");
                await _lifecycleService.StopAsync(plan.InstanceId);
            }

            await UpdateJournalAsync(journal, InstanceUpdateJournalState.Snapshotting, cancellationToken);
            onProgress?.Invoke("Creating update snapshot...");
            journal.SnapshotDirectory = await _rollbackService.CreateSnapshotAsync(plan, cancellationToken);
            await _journalStore.SaveAsync(journal, cancellationToken);

            await UpdateJournalAsync(journal, InstanceUpdateJournalState.RunningWorldBackup, cancellationToken);
            onProgress?.Invoke("Running world backup...");
            await _backupRunner(plan.CurrentMetadata, plan.ServerDir, onProgress);

            await UpdateJournalAsync(journal, InstanceUpdateJournalState.ApplyingServer, cancellationToken);
            onProgress?.Invoke("Applying server update...");
            await ApplyServerArtifactAsync(plan, stagedArtifacts.ServerArtifactPath, cancellationToken);

            await UpdateJournalAsync(journal, InstanceUpdateJournalState.UpdatingMetadata, cancellationToken);
            SaveMetadata(plan.TargetMetadata, plan.ServerDir);

            if (plan.AddonMigrationPlan.Items.Any(item => item.Action is AddonMigrationAction.Update or AddonMigrationAction.AddDependency))
            {
                await UpdateJournalAsync(journal, InstanceUpdateJournalState.ApplyingAddons, cancellationToken);
                onProgress?.Invoke("Applying addon updates...");
                await _addonApplier.ApplyFileSwapsAsync(plan.AddonMigrationPlan, cancellationToken);

                await UpdateJournalAsync(journal, InstanceUpdateJournalState.UpdatingAddonManifest, cancellationToken);
                await _addonApplier.UpdateManifestAsync(plan.AddonMigrationPlan, cancellationToken);
            }

            await UpdateJournalAsync(journal, InstanceUpdateJournalState.Completed, cancellationToken);
            await CleanStagingAsync(stagedArtifacts, cancellationToken);

            return new InstanceUpdateApplyResult
            {
                OperationId = plan.OperationId,
                SnapshotDirectory = journal.SnapshotDirectory,
                RolledBack = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Instance update {OperationId} failed; attempting rollback.", plan.OperationId);
            journal.State = InstanceUpdateJournalState.Failed;
            journal.FailureMessage = ex.Message;
            await _journalStore.SaveAsync(journal, CancellationToken.None);

            if (_rollbackService.HasRollbackBackup(plan.ServerDir))
            {
                await _rollbackService.RollbackAsync(plan.ServerDir, restoreWorldBackup: false, CancellationToken.None);
            }

            throw;
        }
    }

    private async Task ApplyServerArtifactAsync(
        InstanceUpdatePlan plan,
        string stagedServerArtifactPath,
        CancellationToken cancellationToken)
    {
        ValidateNonEmptyFile(stagedServerArtifactPath);

        if (plan.TargetMetadata.ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyBedrockArtifactAsync(plan, stagedServerArtifactPath, cancellationToken);
            return;
        }

        string targetPath = PathSafety.ValidateContainedPath(plan.ServerDir, plan.ServerArtifactFileName)
            ?? throw new InvalidOperationException($"Server artifact target '{plan.ServerArtifactFileName}' is invalid.");
        await FileUtils.CopyFileAsync(stagedServerArtifactPath, targetPath, overwrite: true);

        if (plan.TargetMetadata.ServerType.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
            plan.TargetMetadata.ServerType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            await ClearInstallerOutputsAsync(plan.ServerDir, cancellationToken);
        }
    }

    private async Task ApplyBedrockArtifactAsync(
        InstanceUpdatePlan plan,
        string stagedZipPath,
        CancellationToken cancellationToken)
    {
        string extractionDirectory = PathSafety.ValidateContainedPath(
            Path.GetDirectoryName(stagedZipPath) ?? plan.ServerDir,
            "bedrock-extracted")
            ?? throw new InvalidOperationException("Bedrock staging extraction path is invalid.");

        if (Directory.Exists(extractionDirectory))
        {
            await FileUtils.CleanDirectoryAsync(extractionDirectory, cancellationToken);
        }

        await SafeZipExtractor.ExtractAsync(stagedZipPath, extractionDirectory);

        foreach (string sourcePath in Directory.EnumerateFileSystemEntries(extractionDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(sourcePath);
            if (BedrockPreservedEntries.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetPath = PathSafety.ValidateContainedPath(plan.ServerDir, name)
                ?? throw new InvalidOperationException($"Bedrock update target '{name}' is invalid.");

            if (Directory.Exists(sourcePath))
            {
                if (Directory.Exists(targetPath))
                {
                    await FileUtils.CleanDirectoryAsync(targetPath, cancellationToken);
                }

                await FileUtils.CopyDirectoryAsync(sourcePath, targetPath);
            }
            else
            {
                await FileUtils.CopyFileAsync(sourcePath, targetPath, overwrite: true);
            }
        }
    }

    private static async Task ClearInstallerOutputsAsync(
        string serverDir,
        CancellationToken cancellationToken)
    {
        foreach (string directoryName in new[] { "libraries" })
        {
            string? directory = PathSafety.ValidateContainedPath(serverDir, directoryName);
            if (directory != null && Directory.Exists(directory))
            {
                await FileUtils.CleanDirectoryAsync(directory, cancellationToken);
            }
        }

        foreach (string fileName in new[] { "win_args.txt", "unix_args.txt", "user_jvm_args.txt", "run.bat", "run.sh" })
        {
            string? filePath = PathSafety.ValidateContainedPath(serverDir, fileName);
            if (filePath != null && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        foreach (string jarPath in Directory.EnumerateFiles(serverDir, "*.jar", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(jarPath);
            if (fileName.Equals("installer.jar", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fileName.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(jarPath);
            }
        }
    }

    private void SaveMetadata(InstanceMetadata metadata, string serverDir)
    {
        if (_instanceManager != null)
        {
            _instanceManager.SaveMetadata(metadata, serverDir);
            return;
        }

        string metadataPath = PathSafety.ValidateContainedPath(serverDir, ".pocket-mc.json")
            ?? throw new InvalidOperationException("Metadata path is invalid.");
        FileUtils.AtomicWriteAllText(metadataPath, JsonSerializer.Serialize(metadata, MetadataJsonOptions));
    }

    private async Task UpdateJournalAsync(
        InstanceUpdateJournal journal,
        InstanceUpdateJournalState state,
        CancellationToken cancellationToken)
    {
        journal.State = state;
        await _journalStore.SaveAsync(journal, cancellationToken);
    }

    private static async Task CleanStagingAsync(
        InstanceUpdateStagedArtifacts stagedArtifacts,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(stagedArtifacts.StagingDirectory) &&
            Directory.Exists(stagedArtifacts.StagingDirectory))
        {
            await FileUtils.CleanDirectoryAsync(stagedArtifacts.StagingDirectory, cancellationToken);
        }
    }

    private static void ValidateNonEmptyFile(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length == 0)
        {
            throw new FileNotFoundException($"Staged server artifact '{filePath}' was not found or is empty.", filePath);
        }
    }
}

