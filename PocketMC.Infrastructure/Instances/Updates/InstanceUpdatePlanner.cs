using System.Text.Json;
using System.IO;
using PocketMC.Infrastructure.Java;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances.Updates;

public sealed class InstanceUpdatePlanner
{
    private readonly AddonMigrationPlanner _addonMigrationPlanner;
    private readonly InstanceUpdateJournalStore _journalStore;

    public InstanceUpdatePlanner(
        AddonMigrationPlanner addonMigrationPlanner,
        InstanceUpdateJournalStore journalStore)
    {
        _addonMigrationPlanner = addonMigrationPlanner;
        _journalStore = journalStore;
    }

    public async Task<InstanceUpdatePlan> BuildPlanAsync(
        string serverDir,
        InstanceMetadata currentMetadata,
        string targetMinecraftVersion,
        InstanceUpdateMode updateMode = InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons,
        string? targetLoaderVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentMetadata);
        if (string.IsNullOrWhiteSpace(targetMinecraftVersion))
        {
            throw new ArgumentException("Target Minecraft version is required.", nameof(targetMinecraftVersion));
        }

        InstanceMetadata targetMetadata = CloneMetadata(currentMetadata);
        targetMetadata.MinecraftVersion = targetMinecraftVersion.Trim();
        if (targetLoaderVersion != null)
        {
            targetMetadata.LoaderVersion = targetLoaderVersion;
        }

        var targetCompatibility = new EngineCompatibility(targetMetadata.ServerType);
        int currentJava = JavaRuntimeResolver.GetRequiredJavaVersion(currentMetadata);
        int targetJava = JavaRuntimeResolver.GetRequiredJavaVersion(targetMetadata);

        AddonMigrationPlan addonPlan = await _addonMigrationPlanner.BuildPlanAsync(
            serverDir,
            currentMetadata,
            targetMetadata.MinecraftVersion,
            targetCompatibility,
            updateMode,
            cancellationToken);

        InstanceUpdateJournal? recoverableJournal = await _journalStore.GetLatestRecoverableJournalAsync(serverDir, cancellationToken);

        return new InstanceUpdatePlan
        {
            OperationId = Guid.NewGuid(),
            InstanceId = currentMetadata.Id,
            ServerDir = Path.GetFullPath(serverDir),
            CurrentMetadata = CloneMetadata(currentMetadata),
            TargetMetadata = targetMetadata,
            CurrentMinecraftVersion = currentMetadata.MinecraftVersion,
            TargetMinecraftVersion = targetMetadata.MinecraftVersion,
            TargetCompatibility = targetCompatibility,
            UpdateMode = updateMode,
            CurrentRequiredJavaVersion = currentJava,
            TargetRequiredJavaVersion = targetJava,
            RequiredJavaVersionChangeText = currentJava == targetJava
                ? $"Java {targetJava} remains required"
                : $"Java {currentJava} -> Java {targetJava}",
            ServerArtifactFileName = ResolveServerArtifactFileName(targetMetadata.ServerType),
            ChangelogPreview = $"Update {targetMetadata.ServerType} from {currentMetadata.MinecraftVersion} to {targetMetadata.MinecraftVersion}.",
            RollbackAvailable = recoverableJournal != null,
            AddonMigrationPlan = addonPlan
        };
    }

    public static string ResolveServerArtifactFileName(string serverType)
    {
        if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            return "bedrock-server.zip";
        }

        if (serverType.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
            serverType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            return "installer.jar";
        }

        if (serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
        {
            return "PocketMine-MP.phar";
        }

        return "server.jar";
    }

    private static InstanceMetadata CloneMetadata(InstanceMetadata metadata)
    {
        string json = JsonSerializer.Serialize(metadata);
        return JsonSerializer.Deserialize<InstanceMetadata>(json) ?? new InstanceMetadata();
    }
}
