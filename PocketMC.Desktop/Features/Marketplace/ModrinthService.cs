using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace.Models;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class ModrinthSearchResult
    {
        [JsonPropertyName("hits")]
        public List<ModrinthHit> Hits { get; set; } = new();
    }

    public class ModrinthHit
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";
    }

    public class ModrinthVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ModrinthFile> Files { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();

        [JsonPropertyName("dependencies")]
        public List<ModrinthDependency> Dependencies { get; set; } = new();

        [JsonPropertyName("version_type")]
        public string VersionType { get; set; } = "release";
    }

    public class ModrinthDependency
    {
        [JsonPropertyName("version_id")]
        public string? VersionId { get; set; }

        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("dependency_type")]
        public string DependencyType { get; set; } = ""; // "required", "optional", "incompatible", "embedded"
    }

    public class ModrinthFile
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("filename")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool IsPrimary { get; set; }

        [JsonPropertyName("hashes")]
        public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class ModrinthProject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }
    }

    public class ModrinthService : IAddonProvider
    {
        private readonly HttpClient _httpClient;

        public ModrinthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public string Name => "Modrinth";

        public async Task<List<ModrinthHit>> SearchAsync(string type, string mcVersion, string loader, string sort = "relevance", string query = "", int offset = 0)
        {
            try
            {
                // type is expected as "project_type:plugin" or "project_type:mod" or "project_type:modpack"
                string facetsStr = $"[\"{type}\"]";
                if (type == "project_type:mod")
                {
                    // Ensure we only see mods that can run on a server
                    facetsStr += ",[\"server_side:required\",\"server_side:optional\"]";
                }

                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                {
                    facetsStr += $",[\"versions:{mcVersion}\"]";
                }
                if (type == "project_type:mod" && !string.IsNullOrWhiteSpace(loader))
                {
                    facetsStr += $",[\"categories:{loader}\"]";
                }
                string facets = $"[{facetsStr}]";

                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=20&offset={offset}&index={sort}";

                var result = await _httpClient.GetFromJsonAsync<ModrinthSearchResult>(url);
                return result?.Hits ?? new();
            }
            catch
            {
                return new();
            }
        }

        async Task<MarketplaceVersion?> IAddonProvider.GetLatestVersionAsync(string slug, string mcVersion, string loader)
        {
            var mVersion = await GetLatestVersionAsync(slug, mcVersion, loader);
            if (mVersion == null) return null;

            // We need project title for the UI summary
            var projectInfo = await GetProjectInfoAsync(slug);

            return MapToMarketplaceVersion(mVersion, projectInfo?.Title ?? slug);
        }

        public async Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId)
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/version/{versionId}";
                var mVersion = await _httpClient.GetFromJsonAsync<ModrinthVersion>(url);
                if (mVersion == null) return null;

                var projectInfo = await GetProjectInfoAsync(mVersion.ProjectId);
                return MapToMarketplaceVersion(mVersion, projectInfo?.Title ?? mVersion.ProjectId);
            }
            catch
            {
                return null;
            }
        }

        public async Task<Dictionary<string, ModrinthVersion>> GetVersionsByHashesAsync(IEnumerable<string> hashes)
        {
            try
            {
                var requestBody = new { hashes = hashes.ToList(), algorithm = "sha1" };
                var response = await _httpClient.PostAsJsonAsync("https://api.modrinth.com/v2/version_files", requestBody);
                if (!response.IsSuccessStatusCode) return new();

                return await response.Content.ReadFromJsonAsync<Dictionary<string, ModrinthVersion>>() ?? new();
            }
            catch
            {
                return new();
            }
        }

        public async Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectIdOrSlug)
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/project/{projectIdOrSlug}";
                var project = await _httpClient.GetFromJsonAsync<ModrinthProject>(url);
                if (project == null) return null;

                return new MarketplaceProjectInfo
                {
                    Id = project.Id,
                    Title = project.Title,
                    Slug = project.Slug,
                    IconUrl = project.IconUrl
                };
            }
            catch
            {
                return null;
            }
        }

        private MarketplaceVersion MapToMarketplaceVersion(ModrinthVersion v, string projectTitle)
        {
            var primaryFile = v.Files.FirstOrDefault(f => f.IsPrimary) ?? v.Files.FirstOrDefault() ?? new ModrinthFile();
            
            var result = new MarketplaceVersion
            {
                Id = v.Id,
                Name = v.Name,
                ProjectId = v.ProjectId,
                ProjectTitle = projectTitle,
                FileName = primaryFile.FileName,
                DownloadUrl = primaryFile.Url,
                Hash = GetPreferredHash(primaryFile, out string? hashType),
                HashType = hashType,
                ReleaseType = string.IsNullOrWhiteSpace(v.VersionType) ? "release" : v.VersionType
            };

            if (!result.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"Only a {result.ReleaseType} version is available for this selection. Review before installing.");
            }

            foreach (var dep in v.Dependencies)
            {
                if (string.IsNullOrEmpty(dep.ProjectId)) continue;

                result.Dependencies.Add(new MarketplaceDependency
                {
                    ProjectId = dep.ProjectId,
                    VersionId = dep.VersionId,
                    Type = dep.DependencyType.ToLowerInvariant() switch
                    {
                        "required" => DependencyType.Required,
                        "optional" => DependencyType.Optional,
                        "embedded" => DependencyType.Embedded,
                        "incompatible" => DependencyType.Incompatible,
                        _ => DependencyType.Optional
                    }
                });
            }

            return result;
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string slug, string mcVersion, string? loader = null)
        {
            try
            {
                string baseUrl = $"https://api.modrinth.com/v2/project/{slug}/version";
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                    queryParams.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion }))}");

                if (!string.IsNullOrEmpty(loader))
                    queryParams.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");

                string url = queryParams.Count > 0 ? $"{baseUrl}?{string.Join("&", queryParams)}" : baseUrl;
                var versions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(url);

                if (versions?.Count > 0)
                    return SelectPreferredVersion(versions);

                if (!string.IsNullOrWhiteSpace(loader))
                {
                    // Fallback: some projects have inconsistent loader metadata in indexed filters.
                    string relaxedUrl = !string.IsNullOrEmpty(mcVersion) && mcVersion != "*"
                        ? $"{baseUrl}?game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion }))}"
                        : baseUrl;

                    var relaxedVersions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(relaxedUrl) ?? new();
                    var loaderMatches = relaxedVersions
                        .Where(v => v.Loaders.Any(l => l.Equals(loader, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    if (loaderMatches.Count > 0) return SelectPreferredVersion(loaderMatches);
                    return null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static ModrinthVersion SelectPreferredVersion(IReadOnlyList<ModrinthVersion> versions)
        {
            return versions.FirstOrDefault(v => v.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
                ?? versions[0];
        }

        private static string? GetPreferredHash(ModrinthFile file, out string? hashType)
        {
            if (file.Hashes.TryGetValue("sha512", out string? sha512) && !string.IsNullOrWhiteSpace(sha512))
            {
                hashType = "sha512";
                return sha512;
            }

            if (file.Hashes.TryGetValue("sha1", out string? sha1) && !string.IsNullOrWhiteSpace(sha1))
            {
                hashType = "sha1";
                return sha1;
            }

            hashType = null;
            return null;
        }
    }
}
