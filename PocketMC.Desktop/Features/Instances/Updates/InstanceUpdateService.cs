using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Mods;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Instances.Updates;

public sealed class InstanceUpdateService
{
    private readonly InstanceUpdatePlanner _planner;
    private readonly InstanceArtifactStager _stager;
    private readonly InstanceUpdateApplier _applier;
    private readonly InstanceRollbackService _rollbackService;
    private readonly InstanceUpdateJournalStore _journalStore;
    private readonly InstanceUpdateLockService _lockService;
    private readonly ILogger<InstanceUpdateService> _logger;

    public InstanceUpdateService(
        InstanceUpdatePlanner planner,
        InstanceArtifactStager stager,
        InstanceUpdateApplier applier,
        InstanceRollbackService rollbackService,
        InstanceUpdateJournalStore journalStore,
        InstanceUpdateLockService lockService,
        ILogger<InstanceUpdateService> logger)
    {
        _planner = planner;
        _stager = stager;
        _applier = applier;
        _rollbackService = rollbackService;
        _journalStore = journalStore;
        _lockService = lockService;
        _logger = logger;
    }

    public Task<InstanceUpdatePlan> PlanAsync(
        string serverDir,
        InstanceMetadata metadata,
        string targetMinecraftVersion,
        InstanceUpdateMode updateMode = InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons,
        string? targetLoaderVersion = null,
        CancellationToken cancellationToken = default)
    {
        return _planner.BuildPlanAsync(serverDir, metadata, targetMinecraftVersion, updateMode, targetLoaderVersion, cancellationToken);
    }

    public async Task<InstanceUpdateStagedArtifacts> StageAsync(
        InstanceUpdatePlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var journal = new InstanceUpdateJournal
        {
            OperationId = plan.OperationId,
            InstanceId = plan.InstanceId,
            ServerDir = plan.ServerDir,
            State = InstanceUpdateJournalState.StagingServer
        };
        await _journalStore.SaveAsync(journal, cancellationToken);

        InstanceUpdateStagedArtifacts staged = await _stager.StageAsync(plan, progress, cancellationToken);
        journal.StagingDirectory = staged.StagingDirectory;
        journal.State = InstanceUpdateJournalState.Staged;
        await _journalStore.SaveAsync(journal, cancellationToken);
        return staged;
    }

    public async Task<InstanceUpdateApplyResult> ApplyAsync(
        InstanceUpdatePlan plan,
        InstanceUpdateStagedArtifacts stagedArtifacts,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        using IDisposable updateLock = await _lockService.AcquireAsync(plan.InstanceId, cancellationToken);
        return await _applier.ApplyAsync(plan, stagedArtifacts, onProgress, cancellationToken);
    }

    public async Task<InstanceUpdateApplyResult> UpdateAsync(
        string serverDir,
        InstanceMetadata metadata,
        string targetMinecraftVersion,
        InstanceUpdateMode updateMode = InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons,
        string? targetLoaderVersion = null,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        using IDisposable updateLock = await _lockService.AcquireAsync(metadata.Id, cancellationToken);
        InstanceUpdatePlan plan = await PlanAsync(serverDir, metadata, targetMinecraftVersion, updateMode, targetLoaderVersion, cancellationToken);
        InstanceUpdateStagedArtifacts staged = await StageAsync(plan, progress, cancellationToken);
        return await _applier.ApplyAsync(plan, staged, onProgress, cancellationToken);
    }

    public async Task<bool> RollbackLatestAsync(
        Guid instanceId,
        string serverDir,
        bool restoreWorldBackup = false,
        CancellationToken cancellationToken = default)
    {
        if (!_rollbackService.HasRollbackBackup(serverDir))
        {
            return false;
        }

        using IDisposable updateLock = await _lockService.AcquireAsync(instanceId, cancellationToken);
        _logger.LogInformation("Rolling back update for {ServerDir}.", serverDir);
        await _rollbackService.RollbackAsync(serverDir, restoreWorldBackup, cancellationToken);
        return true;
    }
}

