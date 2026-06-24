using PocketMC.Domain.Models;
namespace PocketMC.Desktop.Features.Mods;

public enum AddonUpdateStatus
{
    Unknown,
    UnknownSource,
    Checking,
    UpToDate,
    UpdateAvailable,
    PossiblyIncompatible,
    ProviderError,
    UnsupportedProvider
}

public sealed class AddonUpdateInfo
{
    public string? LatestVersionId { get; init; }
    public string? LatestVersionName { get; init; }
    public string? LatestFileName { get; init; }
    public string? LatestDownloadUrl { get; init; }
    public string? ProjectTitle { get; init; }
    public string? Hash { get; init; }
    public string? HashType { get; init; }
    public string ReleaseType { get; init; } = "release";
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class AddonUpdateCheckResultModel
{
    public AddonUpdateStatus Status { get; init; } = AddonUpdateStatus.Unknown;
    public string? Message { get; init; }
    public AddonUpdateInfo? UpdateInfo { get; init; }
}

public sealed class AddonProvenance
{
    public string Provider { get; init; } = "Unknown";
    public string? ProjectId { get; init; }
    public string? VersionId { get; init; }
    public string? InstalledFileName { get; init; }
    public string? InstalledFileHash { get; init; }
    public string? InstalledFileHashType { get; init; }
    public string? MinecraftVersion { get; init; }
    public string? Loader { get; init; }
    public DateTime InstalledAtUtc { get; init; }
}
