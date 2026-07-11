using PocketMC.Application.Services.Mods;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketMC.Domain.Storage;
using PocketMC.Domain.Security;

using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Mods;

public sealed class AddonStateDocument
{
    public int Version { get; set; } = 1;
    public List<AddonStateEntry> Entries { get; set; } = new();
}

public sealed class AddonStateEntry
{
    public Guid StableItemId { get; set; }
    public AddonKind Kind { get; set; }
    public string OriginalRelativePath { get; set; } = "";
    public string? DisabledRelativePath { get; set; }
    public string LastKnownDisplayName { get; set; } = "";
    public string LoaderType { get; set; } = "Unknown";
    public string? Version { get; set; }
    public AddonProvenance? Provenance { get; set; }
    public DateTime? LastToggledUtc { get; set; }
    public string? DisabledReason { get; set; }
    public AddonDisabledBySource DisabledBy { get; set; } = AddonDisabledBySource.Unknown;
}

public sealed class AddonStateStore
{
    private const string StateRelativePath = ".pocketmc/addons-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AddonStateDocument> LoadAsync(
        string instanceRoot,
        CancellationToken cancellationToken = default)
    {
        string path = GetStatePath(instanceRoot);
        if (!File.Exists(path))
        {
            return new AddonStateDocument();
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<AddonStateDocument>(json, JsonOptions) ?? new AddonStateDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AddonStateDocument();
        }
    }

    public async Task SaveAsync(
        string instanceRoot,
        AddonStateDocument document,
        CancellationToken cancellationToken = default)
    {
        string path = GetStatePath(instanceRoot);
        string json = JsonSerializer.Serialize(document, JsonOptions);
        await FileUtils.AtomicWriteAllTextAsync(path, json, cancellationToken: cancellationToken);
    }

    public static string GetStatePath(string instanceRoot)
    {
        return PathSafety.ValidateContainedPath(instanceRoot, StateRelativePath)
            ?? throw new InvalidOperationException("Invalid add-on state path.");
    }

    public static AddonStateEntry? FindByRelativePath(
        AddonStateDocument document,
        AddonKind kind,
        string relativePath)
    {
        string normalized = AddonFileNamePolicy.NormalizeRelativePath(relativePath);
        return document.Entries.FirstOrDefault(entry =>
            entry.Kind == kind &&
            (entry.OriginalRelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.DisabledRelativePath, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    public static Guid CreateStableItemId(AddonKind kind, string originalRelativePath)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{kind}:{AddonFileNamePolicy.NormalizeRelativePath(originalRelativePath).ToLowerInvariant()}"));
        return new Guid(bytes);
    }
}
