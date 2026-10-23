using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Backups;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Infrastructure.Mods;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;

using PocketMC.Domain.Storage;
using PocketMC.Domain.Security;

namespace PocketMC.Infrastructure.Instances.Updates;

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

    private readonly IServerLifecycleService _lifecycleService;
    private readonly ILogger<InstanceUpdateApplier> _logger;
    private readonly InstanceManager? _instanceManager;

    public InstanceUpdateApplier(
        IServerLifecycleService lifecycleService,
        InstanceManager instanceManager,
        ILogger<InstanceUpdateApplier> logger)
    {
        _lifecycleService = lifecycleService;
        _instanceManager = instanceManager;
        _logger = logger;
    }

    public async Task<InstanceUpdateApplyResult> ApplyAsync(
        InstanceUpdatePlan plan,
        InstanceUpdateStagedArtifacts stagedArtifacts,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stagedArtifacts);

        if (_lifecycleService.IsRunning(plan.InstanceId))
        {
            onProgress?.Invoke("Stopping server...");
            await _lifecycleService.StopAsync(plan.InstanceId);
        }

        using var operation = await new InstanceOperationCoordinator().AcquireAsync(plan.ServerDir, requiresStopped: true, cancellationToken: cancellationToken);
        if (_lifecycleService.IsRunning(plan.InstanceId)) throw new InvalidOperationException("The server did not stop; update was cancelled.");
        if (InstanceFileTransaction.Recover(plan.ServerDir))
            throw new InvalidOperationException("Interrupted file changes were recovered. Reload the instance and plan the update again.");
        ValidateNonEmptyFile(stagedArtifacts.ServerArtifactPath);
        var replacements = new List<InstanceFileTransaction.Replacement>();
        if (plan.TargetMetadata.ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            string extracted = Path.Combine(stagedArtifacts.StagingDirectory, $"bedrock-{Guid.NewGuid():N}");
            await SafeZipExtractor.ExtractAsync(stagedArtifacts.ServerArtifactPath, extracted, cancellationToken: cancellationToken);
            foreach (string source in Directory.EnumerateFileSystemEntries(extracted))
            {
                string name = Path.GetFileName(source);
                if (!BedrockPreservedEntries.Contains(name, StringComparer.OrdinalIgnoreCase)) replacements.Add(new(source, name));
            }
        }
        else
        {
            replacements.Add(new(stagedArtifacts.ServerArtifactPath, plan.ServerArtifactFileName));
            if (plan.TargetMetadata.ServerType is "Forge" or "NeoForge")
            {
                foreach (string name in new[] { "libraries", "win_args.txt", "unix_args.txt", "user_jvm_args.txt", "run.bat", "run.sh" })
                    replacements.Add(new(null, name));
                foreach (string jar in Directory.EnumerateFiles(plan.ServerDir, "*.jar"))
                    if (Path.GetFileName(jar) != "installer.jar" && Path.GetFileName(jar).Contains("forge", StringComparison.OrdinalIgnoreCase))
                        replacements.Add(new(null, Path.GetFileName(jar)));
            }
        }
        string metadataFile = Path.Combine(stagedArtifacts.StagingDirectory, "updated-metadata.json");
        FileUtils.AtomicWriteAllText(metadataFile, JsonSerializer.Serialize(plan.TargetMetadata, MetadataJsonOptions));
        replacements.Add(new(metadataFile, ".pocket-mc.json"));
        onProgress?.Invoke("Committing verified server update...");
        InstanceFileTransaction.Commit(plan.ServerDir, replacements, cancellationToken);
        SaveMetadata(plan.TargetMetadata, plan.ServerDir);
        await CleanStagingAsync(stagedArtifacts, cancellationToken);

        return new InstanceUpdateApplyResult
        {
            OperationId = plan.OperationId,
            UpdatedMetadata = plan.TargetMetadata
        };
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

