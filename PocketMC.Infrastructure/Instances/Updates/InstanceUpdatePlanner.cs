using System.Text.Json;
using System.IO;
using PocketMC.Infrastructure.Java;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances.Updates;

public sealed class InstanceUpdatePlanner
{
    public InstanceUpdatePlanner()
    {
    }

    public Task<InstanceUpdatePlan> BuildPlanAsync(
        string serverDir,
        InstanceMetadata currentMetadata,
        string targetMinecraftVersion,
        string? targetLoaderVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentMetadata);
        if (string.IsNullOrWhiteSpace(targetMinecraftVersion))
        {
            throw new ArgumentException("Target Minecraft version is required.", nameof(targetMinecraftVersion));
        }

        InstanceMetadata targetMetadata = CloneMetadata(currentMetadata);
        bool mcVersionChanged = !string.Equals(targetMetadata.MinecraftVersion, targetMinecraftVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        
        targetMetadata.MinecraftVersion = targetMinecraftVersion.Trim();
        if (mcVersionChanged)
        {
            targetMetadata.LoaderVersion = string.Empty;
        }
        
        if (targetLoaderVersion != null)
        {
            targetMetadata.LoaderVersion = targetLoaderVersion;
        }

        var targetCompatibility = new EngineCompatibility(targetMetadata.ServerType);
        int currentJava = JavaRuntimeResolver.GetRequiredJavaVersion(currentMetadata);
        int targetJava = JavaRuntimeResolver.GetRequiredJavaVersion(targetMetadata);

        var plan = new InstanceUpdatePlan
        {
            OperationId = Guid.NewGuid(),
            InstanceId = currentMetadata.Id,
            ServerDir = Path.GetFullPath(serverDir),
            CurrentMetadata = CloneMetadata(currentMetadata),
            TargetMetadata = targetMetadata,
            CurrentMinecraftVersion = currentMetadata.MinecraftVersion,
            TargetMinecraftVersion = targetMetadata.MinecraftVersion,
            TargetCompatibility = targetCompatibility,
            CurrentRequiredJavaVersion = currentJava,
            TargetRequiredJavaVersion = targetJava,
            RequiredJavaVersionChangeText = currentJava == targetJava
                ? $"Java {targetJava} remains required"
                : $"Java {currentJava} -> Java {targetJava}",
            ServerArtifactFileName = ResolveServerArtifactFileName(targetMetadata.ServerType)
        };

        return Task.FromResult(plan);
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
