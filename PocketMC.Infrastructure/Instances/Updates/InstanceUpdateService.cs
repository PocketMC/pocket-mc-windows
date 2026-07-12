using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Infrastructure.Mods;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Instances.Updates;

public sealed class InstanceUpdateService
{
    private readonly InstanceUpdatePlanner _planner;
    private readonly InstanceArtifactStager _stager;
    private readonly InstanceUpdateApplier _applier;
    private readonly InstanceUpdateLockService _lockService;
    private readonly ILogger<InstanceUpdateService> _logger;

    public InstanceUpdateService(
        InstanceUpdatePlanner planner,
        InstanceArtifactStager stager,
        InstanceUpdateApplier applier,
        InstanceUpdateLockService lockService,
        ILogger<InstanceUpdateService> logger)
    {
        _planner = planner;
        _stager = stager;
        _applier = applier;
        _lockService = lockService;
        _logger = logger;
    }

    public Task<InstanceUpdatePlan> PlanAsync(
        string serverDir,
        InstanceMetadata metadata,
        string targetMinecraftVersion,
        string? targetLoaderVersion = null,
        CancellationToken cancellationToken = default)
    {
        return _planner.BuildPlanAsync(serverDir, metadata, targetMinecraftVersion, targetLoaderVersion, cancellationToken);
    }

    public Task<InstanceUpdateStagedArtifacts> StageAsync(
        InstanceUpdatePlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _stager.StageAsync(plan, progress, cancellationToken);
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
        string? targetLoaderVersion = null,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        using IDisposable updateLock = await _lockService.AcquireAsync(metadata.Id, cancellationToken);
        InstanceUpdatePlan plan = await PlanAsync(serverDir, metadata, targetMinecraftVersion, targetLoaderVersion, cancellationToken);
        InstanceUpdateStagedArtifacts staged = await StageAsync(plan, progress, cancellationToken);
        return await _applier.ApplyAsync(plan, staged, onProgress, cancellationToken);
    }
}

