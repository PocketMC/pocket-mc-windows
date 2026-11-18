using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace PocketMC.Infrastructure.Instances.Providers;

/// <summary>
/// Caches provider index documents (Forge/NeoForge/Fabric) so opening a version picker does not
/// re-download multi-megabyte metadata on every keystroke or page visit.
/// </summary>
internal static class ProviderMetaCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    public static async Task<JsonObject?> GetObjectAsync(
        HttpClient client, string url, CancellationToken cancellationToken = default)
    {
        string? payload = await GetRawAsync(client, url, cancellationToken).ConfigureAwait(false);
        return payload is null ? null : JsonNode.Parse(payload) as JsonObject;
    }

    public static async Task<JsonArray?> GetArrayAsync(
        HttpClient client, string url, CancellationToken cancellationToken = default)
    {
        string? payload = await GetRawAsync(client, url, cancellationToken).ConfigureAwait(false);
        return payload is null ? null : JsonNode.Parse(payload) as JsonArray;
    }

    public static async Task<string?> GetRawAsync(
        HttpClient client, string url, CancellationToken cancellationToken = default)
    {
        if (Entries.TryGetValue(url, out Entry? cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Payload;
        }

        string? payload = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(payload))
        {
            Entries[url] = new Entry(payload, DateTimeOffset.UtcNow + Ttl);
        }

        return payload;
    }

    /// <summary>Drops every cached document; used when the user explicitly refreshes a version list.</summary>
    public static void Clear() => Entries.Clear();

    private sealed record Entry(string Payload, DateTimeOffset ExpiresAt);
}
