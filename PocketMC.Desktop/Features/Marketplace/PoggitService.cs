using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using PocketMC.Desktop.Features.Marketplace.Models;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class PoggitService : IAddonProvider
    {
        private readonly HttpClient _http;

        public string Name => "Poggit";

        public PoggitService(IHttpClientFactory httpClientFactory)
        {
            _http = httpClientFactory.CreateClient("PocketMC.Poggit");
        }

        public async Task<List<ModrinthHit>> SearchAsync(string query, int offset)
        {
            string url = "https://poggit.pmmp.io/releases.json";
            if (!string.IsNullOrWhiteSpace(query))
            {
                url += $"?name=*{Uri.EscapeDataString(query)}*";
            }

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<JsonElement>(content);

            var hits = new List<ModrinthHit>();

            if (items.ValueKind == JsonValueKind.Array)
            {
                int skip = offset;
                int taken = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }

                    if (taken >= 20) break;

                    string name = item.GetProperty("name").GetString() ?? "Unknown";
                    string slug = item.GetProperty("name").GetString() ?? ""; 
                    string desc = item.TryGetProperty("tagline", out var t) ? t.GetString() ?? "" : "";
                    string icon = item.TryGetProperty("icon_url", out var i) && i.ValueKind != JsonValueKind.Null ? i.GetString() ?? "" : "";
                    string author = item.GetProperty("repo_name").GetString()?.Split('/')[0] ?? "Unknown";
                    int downloads = item.TryGetProperty("downloads", out var d) ? d.GetInt32() : 0;

                    hits.Add(new ModrinthHit
                    {
                        Slug = slug,
                        Title = name,
                        Description = desc,
                        IconUrl = icon,
                        Downloads = downloads
                    });
                    taken++;
                }
            }

            return hits;
        }

        public async Task<MarketplaceVersion?> GetLatestVersionAsync(string name)
        {
            return await ((IAddonProvider)this).GetLatestVersionAsync(name, "", "");
        }

        async Task<MarketplaceVersion?> IAddonProvider.GetLatestVersionAsync(string projectId, string mcVersion, string loader)
        {
            string url = $"https://poggit.pmmp.io/releases.json?name={Uri.EscapeDataString(projectId)}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<JsonElement>(content);

            if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                var latest = items[0];
                string vUrl = latest.TryGetProperty("artifact_url", out var a) ? a.GetString() ?? "" : "";
                string displayVersion = TryGetString(latest, "version") ?? "Unknown";
                string versionId = TryGetString(latest, "version")
                    ?? TryGetString(latest, "id")
                    ?? TryGetString(latest, "build")
                    ?? BuildStableUnknownVersionId(vUrl, projectId);
                bool compatibilityKnown = TryReadCompatibility(latest).Count > 0;

                if (!string.IsNullOrEmpty(vUrl))
                {
                    var version = new MarketplaceVersion
                    {
                        Id = versionId,
                        Name = displayVersion,
                        ProjectId = projectId,
                        ProjectTitle = projectId,
                        FileName = $"{projectId}.phar",
                        DownloadUrl = vUrl,
                        ReleaseType = "release"
                    };

                    if (!compatibilityKnown)
                    {
                        version.Warnings.Add("Poggit compatibility could not be verified because API compatibility metadata is unavailable.");
                    }

                    return version;
                }
            }

            return null;
        }

        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId) => Task.FromResult<MarketplaceVersion?>(null);

        public async Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId)
        {
             return new MarketplaceProjectInfo
             {
                 Id = projectId,
                 Title = projectId,
                 Slug = projectId
             };
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static IReadOnlyList<string> TryReadCompatibility(JsonElement element)
        {
            foreach (string propertyName in new[] { "api", "api_versions", "apiVersion", "api_version" })
            {
                if (!element.TryGetProperty(propertyName, out JsonElement value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .ToArray();
                }

                string? singleValue = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                return string.IsNullOrWhiteSpace(singleValue) ? Array.Empty<string>() : new[] { singleValue };
            }

            return Array.Empty<string>();
        }

        private static string BuildStableUnknownVersionId(string artifactUrl, string projectId)
        {
            string seed = string.IsNullOrWhiteSpace(artifactUrl) ? projectId : artifactUrl;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            return $"unknown-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
        }
    }
}
