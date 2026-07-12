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

        onProgress?.Invoke("Applying server update...");
        await ApplyServerArtifactAsync(plan, stagedArtifacts.ServerArtifactPath, cancellationToken);

        SaveMetadata(plan.TargetMetadata, plan.ServerDir);

        await CleanStagingAsync(stagedArtifacts, cancellationToken);

        return new InstanceUpdateApplyResult
        {
            OperationId = plan.OperationId
        };
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

