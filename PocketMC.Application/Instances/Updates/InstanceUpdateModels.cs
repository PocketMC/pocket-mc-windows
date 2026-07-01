using PocketMC.Domain.Models;

namespace PocketMC.Application.Instances.Updates;

public enum InstanceUpdateMode
{
    ServerOnlyWarnAboutAddons,
    ServerAndCompatibleMarketplaceAddons,
    ServerAndCompatibleMarketplaceAddonsAndDependencies,
    ExperimentalAggressiveUpdate
}

public enum AddonMigrationAction
{
    Update,
    AddDependency,
    Keep,
    WarnOnly
}

public enum AddonMigrationWarningCode
{
    ManualAddonNotUpdated,
    BedrockAutomaticUpdateUnsupported,
    NoCompatibleUpdate,
    UnknownProvider,
    DependencyResolutionFailed,
    ServerOnlyAddonUpdatesSkipped,
    AggressiveUpdate
}

public sealed class AddonMigrationWarning
{
    public AddonMigrationWarningCode Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ProjectId { get; set; }
}

public sealed class AddonMigrationItem
{
    public AddonMigrationAction Action { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string CurrentVersionId { get; set; } = string.Empty;
    public string TargetVersionId { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = string.Empty;
    public string TargetSubDirectory { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? Hash { get; set; }
    public string? HashType { get; set; }
    public string VersionName { get; set; } = string.Empty;
    public string? StagedFilePath { get; set; }
    public bool IsDependency { get; set; }
}

public sealed class AddonMigrationPlan
{
    public string ServerDir { get; set; } = string.Empty;
    public string TargetMinecraftVersion { get; set; } = string.Empty;
    public string TargetLoaderName { get; set; } = string.Empty;
    public EngineCompatibility TargetCompatibility { get; set; } = new("Vanilla");
    public InstanceUpdateMode UpdateMode { get; set; } = InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons;

    public int TrackedAddonCount { get; set; }
    public int ManualUntrackedAddonCount { get; set; }
    public int CompatibleUpdateCount { get; set; }
    public int IncompatibleAddonCount { get; set; }
    public int DependencyAdditionCount { get; set; }

    public List<AddonMigrationItem> Items { get; set; } = new();
    public List<AddonMigrationWarning> Warnings { get; set; } = new();
}

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
    public InstanceUpdateMode UpdateMode { get; set; } = InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons;
    public int CurrentRequiredJavaVersion { get; set; }
    public int TargetRequiredJavaVersion { get; set; }
    public string RequiredJavaVersionChangeText { get; set; } = string.Empty;
    public string ServerArtifactFileName { get; set; } = "server.jar";
    public string ChangelogPreview { get; set; } = string.Empty;
    public bool RollbackAvailable { get; set; }
    public AddonMigrationPlan AddonMigrationPlan { get; set; } = new();
}

public sealed class InstanceUpdateStagedArtifacts
{
    public string StagingDirectory { get; set; } = string.Empty;
    public string ServerArtifactPath { get; set; } = string.Empty;
    public string AddonStagingDirectory { get; set; } = string.Empty;
}

public sealed class InstanceUpdateApplyResult
{
    public Guid OperationId { get; set; }
    public string SnapshotDirectory { get; set; } = string.Empty;
    public bool RolledBack { get; set; }
}

public enum InstanceUpdateJournalState
{
    Planned,
    StagingServer,
    StagingAddons,
    Staged,
    StoppingServer,
    Snapshotting,
    RunningWorldBackup,
    ApplyingServer,
    UpdatingMetadata,
    ApplyingAddons,
    UpdatingAddonManifest,
    Completed,
    Failed,
    RollingBack,
    RolledBack
}

public sealed class InstanceUpdateJournal
{
    public Guid OperationId { get; set; }
    public Guid InstanceId { get; set; }
    public string ServerDir { get; set; } = string.Empty;
    public string SnapshotDirectory { get; set; } = string.Empty;
    public string StagingDirectory { get; set; } = string.Empty;
    public InstanceUpdateJournalState State { get; set; } = InstanceUpdateJournalState.Planned;
    public string? FailureMessage { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
