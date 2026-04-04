using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Services
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
    }

    public class ModrinthVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ModrinthFile> Files { get; set; } = new();
    }

    public class ModrinthFile
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("filename")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool IsPrimary { get; set; }
    }

    public class ModrinthService
    {
        private readonly HttpClient _httpClient;

        public ModrinthService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
        }

        public async Task<List<ModrinthHit>> SearchAsync(string type, string mcVersion, string sort = "relevance")
        {
            try
            {
                // type is expected as "project_type:plugin" or "project_type:mod"
                string facets = $"[[\"{type}\"],[\"versions:{mcVersion}\"]]";
                string url = $"https://api.modrinth.com/v2/search?facets={Uri.EscapeDataString(facets)}&limit=20&index={sort}";
                
                var result = await _httpClient.GetFromJsonAsync<ModrinthSearchResult>(url);
                return result?.Hits ?? new();
            }
            catch
            {
                return new();
            }
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string slug, string mcVersion)
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/project/{slug}/version?game_versions=[\"{mcVersion}\"]";
                var versions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(url);
                
                // Return the first (latest) version
                return versions?.Count > 0 ? versions[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
