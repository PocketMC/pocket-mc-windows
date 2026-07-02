using PocketMC.Domain.Models;
using PocketMC.Application.Instances.Services;
using PocketMC.Application.Instances.Providers;
using PocketMC.Desktop.Features.Mods;
using System.IO;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Application.Instances.Updates;

public sealed class InstanceArtifactStager
{
    private readonly VanillaProvider _vanillaProvider;
    private readonly FabricProvider _fabricProvider;
    private readonly ForgeProvider _forgeProvider;
    private readonly NeoForgeProvider _neoForgeProvider;
    private readonly PaperProvider _paperProvider;
    private readonly PocketmineProvider _pocketmineProvider;
    private readonly BedrockBdsProvider _bedrockProvider;
    private readonly AddonMigrationStager _addonStager;
    private readonly InstanceUpdateJournalStore _journalStore;

    public InstanceArtifactStager(
        VanillaProvider vanillaProvider,
        FabricProvider fabricProvider,
        ForgeProvider forgeProvider,
        NeoForgeProvider neoForgeProvider,
        PaperProvider paperProvider,
        PocketmineProvider pocketmineProvider,
        BedrockBdsProvider bedrockProvider,
        AddonMigrationStager addonStager,
        InstanceUpdateJournalStore journalStore)
    {
        _vanillaProvider = vanillaProvider;
        _fabricProvider = fabricProvider;
        _forgeProvider = forgeProvider;
        _neoForgeProvider = neoForgeProvider;
        _paperProvider = paperProvider;
        _pocketmineProvider = pocketmineProvider;
        _bedrockProvider = bedrockProvider;
        _addonStager = addonStager;
        _journalStore = journalStore;
    }

    public async Task<InstanceUpdateStagedArtifacts> StageAsync(
        InstanceUpdatePlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string stagingDirectory = _journalStore.GetOperationRoot(plan.ServerDir, "staging", plan.OperationId);
        if (Directory.Exists(stagingDirectory))
        {
            await FileUtils.CleanDirectoryAsync(stagingDirectory, cancellationToken);
        }

        string serverStagingDirectory = ResolveStagingDirectory(stagingDirectory, "server");
        string addonStagingDirectory = ResolveStagingDirectory(stagingDirectory, "addons");
        Directory.CreateDirectory(serverStagingDirectory);
        Directory.CreateDirectory(addonStagingDirectory);

        string serverArtifactPath = PathSafety.ValidateContainedPath(serverStagingDirectory, plan.ServerArtifactFileName)
            ?? throw new InvalidOperationException($"Server artifact file name '{plan.ServerArtifactFileName}' is invalid.");

        await DownloadServerArtifactAsync(plan, serverArtifactPath, progress, cancellationToken);
        ValidateNonEmptyFile(serverArtifactPath);

        await _addonStager.StageAsync(plan.AddonMigrationPlan, addonStagingDirectory, progress, cancellationToken);

        return new InstanceUpdateStagedArtifacts
        {
            StagingDirectory = stagingDirectory,
            ServerArtifactPath = serverArtifactPath,
            AddonStagingDirectory = addonStagingDirectory
        };
    }

    private async Task DownloadServerArtifactAsync(
        InstanceUpdatePlan plan,
        string serverArtifactPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string serverType = plan.TargetMetadata.ServerType;
        string targetVersion = plan.TargetMinecraftVersion;
        string loaderVersion = plan.TargetMetadata.LoaderVersion;

        if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            await _bedrockProvider.DownloadSoftwareAsync(targetVersion, serverArtifactPath, progress, cancellationToken);
        }
        else if (serverType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(loaderVersion))
        {
            await _fabricProvider.DownloadFabricJarAsync(targetVersion, loaderVersion, serverArtifactPath, progress, cancellationToken);
        }
        else if (serverType.Equals("Forge", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(loaderVersion))
        {
            await _forgeProvider.DownloadForgeJarAsync(targetVersion, loaderVersion, serverArtifactPath, progress, cancellationToken);
        }
        else if (serverType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(loaderVersion))
        {
            await _neoForgeProvider.DownloadNeoForgeJarAsync(targetVersion, loaderVersion, serverArtifactPath, progress, cancellationToken);
        }
        else
        {
            IServerSoftwareProvider provider = ResolveProvider(serverType);
            await provider.DownloadSoftwareAsync(targetVersion, serverArtifactPath, progress, cancellationToken);
        }
    }

    private IServerSoftwareProvider ResolveProvider(string serverType)
    {
        if (serverType.StartsWith("Paper", StringComparison.OrdinalIgnoreCase) ||
            serverType.StartsWith("Spigot", StringComparison.OrdinalIgnoreCase))
        {
            return _paperProvider;
        }

        if (serverType.StartsWith("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return _fabricProvider;
        }

        if (serverType.StartsWith("Forge", StringComparison.OrdinalIgnoreCase))
        {
            return _forgeProvider;
        }

        if (serverType.StartsWith("NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            return _neoForgeProvider;
        }

        if (serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
        {
            return _pocketmineProvider;
        }

        if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            return _bedrockProvider;
        }

        return _vanillaProvider;
    }

    private static string ResolveStagingDirectory(string stagingRoot, string child)
    {
        return PathSafety.ValidateContainedPath(stagingRoot, child)
            ?? throw new InvalidOperationException($"Staging directory '{child}' is invalid.");
    }

    private static void ValidateNonEmptyFile(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length == 0)
        {
            throw new IOException($"Staged server artifact '{filePath}' is missing or empty.");
        }
    }
}

