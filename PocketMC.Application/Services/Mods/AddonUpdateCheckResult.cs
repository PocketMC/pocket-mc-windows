using System.Collections.Generic;

namespace PocketMC.Application.Services.Mods;

/// <summary>
/// Result of an update check for a single installed addon.
/// </summary>
public class AddonUpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string? LatestVersionId { get; set; }
    public string? LatestVersionName { get; set; }
    public string? LatestFileName { get; set; }
    public string? LatestDownloadUrl { get; set; }
    public string? ProjectTitle { get; set; }
    public string? Hash { get; set; }
    public string? HashType { get; set; }
    public string ReleaseType { get; set; } = "release";
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
}
