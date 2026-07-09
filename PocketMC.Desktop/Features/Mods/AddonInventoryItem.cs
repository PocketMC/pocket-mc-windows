namespace PocketMC.Desktop.Features.Mods;

public sealed class AddonInventoryItem
{
    public Guid InstanceId { get; init; }
    public AddonKind Kind { get; init; }
    public AddonState State { get; init; }
    public string DisplayName { get; init; } = "";
    public string FileName { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string? DisabledPath { get; init; }
    public string LoaderType { get; init; } = "Unknown";
    public string? Version { get; init; }
    public string? ModId { get; init; }
    public ModSideSupport SideSupport { get; init; } = ModSideSupport.Unknown;
    public string SideLabel { get; init; } = "Side unknown";
    public byte[]? IconBytes { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    public AddonUpdateStatus UpdateStatus { get; init; } = AddonUpdateStatus.Unknown;
    public AddonUpdateInfo? UpdateInfo { get; init; }
    public bool CanEnable { get; init; }
    public bool CanDisable { get; init; }
    public bool RequiresServerStopped { get; init; } = true;
    public AddonProvenance? Provenance { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModifiedUtc { get; init; }
}
