using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Services
{
    public class CurseForgeService
    {
        private readonly HttpClient _httpClient;
        private const string ProxyBase = "https://api.curse.tools/v1/cf";

        public CurseForgeService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public async Task<List<ModrinthHit>> SearchAsync(string type, string mcVersion, string query = "", int offset = 0)
        {
            try
            {
                // Map Modrinth project types to CurseForge class IDs
                // 432 is Minecraft
                // Mods: 6, Modpacks: 4471, Bukkit Plugins: 5
                string classId = type switch
                {
                    "project_type:mod" => "6",
                    "project_type:modpack" => "4471",
                    "project_type:plugin" => "5",
                    _ => "6"
                };

                // Correct base URL might need to be carefully checked. 
                // index parameter must be exactly what the v1 API expects.
                string url = $"{ProxyBase}/mods/search?gameId=432&classId={classId}&sortField=2&sortOrder=desc&pageSize=20&index={offset}";
                
                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                    url += $"&gameVersion={mcVersion}";
                
                if (!string.IsNullOrEmpty(query))
                    url += $"&searchFilter={Uri.EscapeDataString(query)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/json");
                
                var httpResponse = await _httpClient.SendAsync(request);
                if (!httpResponse.IsSuccessStatusCode) return new List<ModrinthHit>();

                var response = await httpResponse.Content.ReadFromJsonAsync<JsonObject>();
                var data = response?["data"]?.AsArray();
                var results = new List<ModrinthHit>();

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        if (item == null) continue;
                        
                        // CurseForge logo can be null or have different fields
                        string? icon = item["logo"]?["thumbnailUrl"]?.ToString();
                        if (string.IsNullOrEmpty(icon)) icon = item["logo"]?["url"]?.ToString();

                        results.Add(new ModrinthHit
                        {
                            Title = item["name"]?.ToString() ?? "Unknown",
                            Description = item["summary"]?.ToString() ?? "",
                            IconUrl = icon,
                            Slug = item["id"]?.ToString() ?? "", 
                            Downloads = item["downloadCount"] != null ? (int)item["downloadCount"]! : 0
                        });
                    }
                }
                return results;
            }
            catch (Exception)
            {
                return new List<ModrinthHit>();
            }
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string mcVersion)
        {
            try
            {
                // Get all files for the project
                string url = $"{ProxyBase}/mods/{projectId}/files";
                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                    url += $"?gameVersion={mcVersion}";

                var response = await _httpClient.GetFromJsonAsync<JsonObject>(url);
                var files = response?["data"]?.AsArray();
                
                if (files == null || files.Count == 0) return null;

                // Sort by date or just take the first one (usually latest)
                var latestFile = files[0];
                if (latestFile == null) return null;

                return new ModrinthVersion
                {
                    Id = latestFile["id"]?.ToString() ?? "",
                    Name = latestFile["displayName"]?.ToString() ?? "Latest",
                    Files = new List<ModrinthFile>
                    {
                        new ModrinthFile
                        {
                            Url = latestFile["downloadUrl"]?.ToString() ?? "",
                            FileName = latestFile["fileName"]?.ToString() ?? "mod.jar",
                            IsPrimary = true
                        }
                    }
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
