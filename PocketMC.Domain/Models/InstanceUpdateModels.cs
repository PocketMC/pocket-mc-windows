using PocketMC.Domain.Models;

namespace PocketMC.Domain.Models;

public sealed class InstanceUpdatePlan
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public string ServerDir { get; set; } = string.Empty;
    public InstanceMetadata CurrentMetadata { get; set; } = new();
    public InstanceMetadata TargetMetadata { get; set; } = new();
    public string CurrentMinecraftVersion { get; set; } = string.Empty;
    public string TargetMinecraftVersion { get; set; } = string.Empty;
    public EngineCompatibility TargetCompatibility { get; set; } = new("Vanilla");
    public int CurrentRequiredJavaVersion { get; set; }
    public int TargetRequiredJavaVersion { get; set; }
    public string RequiredJavaVersionChangeText { get; set; } = string.Empty;
    public string ServerArtifactFileName { get; set; } = "server.jar";
}

public sealed class InstanceUpdateStagedArtifacts
{
    public string StagingDirectory { get; set; } = string.Empty;
    public string ServerArtifactPath { get; set; } = string.Empty;
}

public sealed class InstanceUpdateApplyResult
{
    public Guid OperationId { get; set; }
}
