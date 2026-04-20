using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocketMC.Desktop.Features.Mods;

/// <summary>
/// Stores metadata for addons installed into a target directory (mods/plugins/etc.).
/// </summary>
public sealed class AddonManifest
{
    private const string ManifestFileName = ".pocketmc-addon-manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Loads the current addon manifest entries for the provided directory.
    /// </summary>
    public async Task<IReadOnlyList<AddonManifestEntry>> LoadAsync(string addonsDirectory, CancellationToken cancellationToken = default)
    {
        string manifestPath = GetManifestPath(addonsDirectory);
        if (!File.Exists(manifestPath))
        {
            return Array.Empty<AddonManifestEntry>();
        }

        await using FileStream stream = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        List<AddonManifestEntry>? entries = await JsonSerializer.DeserializeAsync<List<AddonManifestEntry>>(stream, JsonOptions, cancellationToken);
        return entries ?? Array.Empty<AddonManifestEntry>();
    }

    /// <summary>
    /// Persists the supplied entries as the manifest for the provided directory.
    /// </summary>
    public async Task SaveAsync(string addonsDirectory, IReadOnlyCollection<AddonManifestEntry> entries, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(addonsDirectory);
        string manifestPath = GetManifestPath(addonsDirectory);

        await using FileStream stream = new(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Removes entries whose backing files were deleted and writes the cleaned manifest.
    /// </summary>
    public async Task<IReadOnlyList<AddonManifestEntry>> SyncManifestAsync(string addonsDirectory, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AddonManifestEntry> existing = await LoadAsync(addonsDirectory, cancellationToken);
        AddonManifestEntry[] filtered = existing
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FileName))
            .Where(entry => File.Exists(Path.Combine(addonsDirectory, entry.FileName)))
            .ToArray();

        await SaveAsync(addonsDirectory, filtered, cancellationToken);
        return filtered;
    }

    private static string GetManifestPath(string addonsDirectory) =>
        Path.Combine(addonsDirectory, ManifestFileName);
}

public sealed class AddonManifestEntry
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("installedAt")]
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
}
