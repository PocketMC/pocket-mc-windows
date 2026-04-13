using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class CurseForgeService
    {
        private readonly HttpClient _httpClient;
        private readonly ApplicationState _appState;
        private const string ApiBase = "https://api.curseforge.com/v1";

        public CurseForgeService(ApplicationState appState, HttpClient httpClient)
        {
            _appState = appState;
            _httpClient = httpClient;
        }

        private string? GetActiveApiKey()
        {
            return !string.IsNullOrWhiteSpace(_appState.Settings.CurseForgeApiKey)
                ? _appState.Settings.CurseForgeApiKey
                : null;
        }

        public async Task<List<ModrinthHit>> SearchAsync(string type, string mcVersion, string query = "", int offset = 0)
        {
            try
            {
                string? apiKey = GetActiveApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    return new List<ModrinthHit>
                    {
                        new ModrinthHit
                        {
                            Title = "CurseForge Key Required",
                            Description = "Please provide your CurseForge API key in the App Settings page to use this source.",
                            IconUrl = "",
                            Slug = "",
                            Downloads = 0
                        }
                    };
                }

                string classId = type switch
                {
                    "project_type:mod" => "6",
                    "project_type:modpack" => "4471",
                    "project_type:plugin" => "5",
                    "project_type:world" => "17",
                    _ => "6"
                };

                string url = $"{ApiBase}/mods/search?gameId=432&classId={classId}&sortField=2&sortOrder=desc&pageSize=20&index={offset}";

                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                    url += $"&gameVersion={Uri.EscapeDataString(mcVersion)}";

                if (!string.IsNullOrEmpty(query))
                    url += $"&searchFilter={Uri.EscapeDataString(query)}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", apiKey);

                var httpResponse = await _httpClient.SendAsync(request);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errorText = await httpResponse.Content.ReadAsStringAsync();
                    return new List<ModrinthHit>
                    {
                        new ModrinthHit
                        {
                            Title = $"API Error: {(int)httpResponse.StatusCode} {httpResponse.StatusCode}",
                            Description = errorText.Length > 150 ? errorText.Substring(0, 150) + "..." : errorText,
                            IconUrl = "",
                            Slug = "",
                            Downloads = 0
                        }
                    };
                }

                var rootNode = await httpResponse.Content.ReadFromJsonAsync<JsonNode>();
                if (rootNode == null) return new List<ModrinthHit>();

                var data = rootNode["data"]?.AsArray();
                var results = new List<ModrinthHit>();

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        if (item == null) continue;

                        string icon = "";
                        var logoNode = item["logo"];
                        if (logoNode is JsonObject logoObj)
                        {
                            icon = logoObj["thumbnailUrl"]?.ToString() ?? logoObj["url"]?.ToString() ?? "";
                        }

                        int safeDownloads = 0;
                        var dlNode = item["downloadCount"];
                        if (dlNode != null && double.TryParse(dlNode.ToString(), out double parsedDl))
                        {
                            safeDownloads = parsedDl > int.MaxValue ? int.MaxValue : (int)parsedDl;
                        }

                        results.Add(new ModrinthHit
                        {
                            Title = item["name"]?.ToString() ?? "Unknown",
                            Description = item["summary"]?.ToString() ?? "",
                            IconUrl = icon,
                            Slug = item["id"]?.ToString() ?? "",
                            Downloads = safeDownloads
                        });
                    }
                }

                if (results.Count == 0)
                {
                    results.Add(new ModrinthHit
                    {
                        Title = "No Results",
                        Description = "The API returned 0 mods for this query/version.",
                        IconUrl = "",
                        Slug = ""
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                return new List<ModrinthHit>
                {
                    new ModrinthHit
                    {
                        Title = "Code Exception",
                        Description = ex.Message,
                        IconUrl = "",
                        Slug = ""
                    }
                };
            }
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(projectId)) return null;

                string? apiKey = GetActiveApiKey();
                if (string.IsNullOrEmpty(apiKey)) return null;

                string url = $"{ApiBase}/mods/{projectId}/files";
                if (!string.IsNullOrEmpty(mcVersion) && mcVersion != "*")
                    url += $"?gameVersion={Uri.EscapeDataString(mcVersion)}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", apiKey);

                var httpResponse = await _httpClient.SendAsync(request);

                if (!httpResponse.IsSuccessStatusCode) return null;

                var rootNode = await httpResponse.Content.ReadFromJsonAsync<JsonNode>();
                var files = rootNode?["data"]?.AsArray();

                if (files == null || files.Count == 0) return null;

                var latestFile = files[0];
                if (latestFile == null) return null;

                long fileId = 0;
                if (long.TryParse(latestFile["id"]?.ToString(), out long parsedId))
                {
                    fileId = parsedId;
                }

                string fileName = latestFile["fileName"]?.ToString() ?? "mod.jar";
                string downloadUrl = latestFile["downloadUrl"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(downloadUrl) && fileId > 0)
                {
                    string part1 = (fileId / 1000).ToString();
                    string part2 = (fileId % 1000).ToString("D3");
                    downloadUrl = $"https://edge.forgecdn.net/files/{part1}/{part2}/{Uri.EscapeDataString(fileName)}";
                }

                return new ModrinthVersion
                {
                    Id = fileId.ToString(),
                    Name = latestFile["displayName"]?.ToString() ?? "Latest",
                    Files = new List<ModrinthFile>
                    {
                        new ModrinthFile
                        {
                            Url = downloadUrl,
                            FileName = fileName,
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
