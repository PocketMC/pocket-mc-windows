using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Marketplace.Models;
using System.Linq;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class CurseForgeService : IAddonProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ApplicationState _appState;
        private const string ApiBase = "https://api.curseforge.com/v1";

        public CurseForgeService(ApplicationState appState, HttpClient httpClient)
        {
            _appState = appState;
            _httpClient = httpClient;
        }

        public string Name => "CurseForge";

        private string? GetActiveApiKey()
        {
            return !string.IsNullOrWhiteSpace(_appState.Settings.CurseForgeApiKey)
                ? _appState.Settings.CurseForgeApiKey
                : null;
        }

        private static int MapLoaderType(string loader) => loader.ToLowerInvariant() switch
        {
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            "neoforge" => 6,
            _ => 0
        };

        private static bool FileSupportsLoader(JsonNode fileNode, string loader)
        {
            if (string.IsNullOrWhiteSpace(loader)) return true;

            var normalizedLoader = loader.ToLowerInvariant();
            var gameVersions = fileNode["gameVersions"]?.AsArray();
            if (gameVersions == null || gameVersions.Count == 0) return false;

            foreach (var gameVersion in gameVersions)
            {
                var value = gameVersion?.ToString();
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (value.Equals(normalizedLoader, StringComparison.OrdinalIgnoreCase) ||
                    value.Contains($"-{normalizedLoader}", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains($"{normalizedLoader}-", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var sortableVersions = fileNode["sortableGameVersions"]?.AsArray();
            if (sortableVersions != null)
            {
                foreach (var sortable in sortableVersions)
                {
                    var value = sortable?["gameVersionName"]?.ToString();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (value.Equals(normalizedLoader, StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($"-{normalizedLoader}", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($"{normalizedLoader}-", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            var fileName = fileNode["fileName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(fileName) &&
                fileName.Contains(normalizedLoader, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public async Task<List<ModrinthHit>> SearchAsync(string type, string mcVersion, string loader, string query = "", int offset = 0)
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
                    "6945" => "6945", 
                    _ => "6"
                };

                string url = $"{ApiBase}/mods/search?gameId=432&classId={classId}&sortField=2&sortOrder=desc&pageSize=20&index={offset}";
                if (classId == "6")
                {
                    url += $"&modLoaderType={MapLoaderType(loader)}";
                }

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

        async Task<MarketplaceVersion?> IAddonProvider.GetLatestVersionAsync(string projectId, string mcVersion, string loader)
        {
            var result = await GetLatestVersionWithProjectInfoAsync(projectId, mcVersion, loader);
            if (result == null) return null;

            return MapToMarketplaceVersion(result.Value.File, result.Value.Project);
        }

        public async Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId)
        {
            // versionId in CurseForge refers to the file ID.
            // We need to fetch the file details, but usually we need the project ID too.
            // CurseForge API allows fetching a single file by ID.
            try
            {
                string? apiKey = GetActiveApiKey();
                if (string.IsNullOrEmpty(apiKey)) return null;

                // We don't have project ID easily if we only have file ID, unless we search or use another endpoint.
                // However, our resolver usually has both Project ID and Version ID from the parent's dependency node.
                // Let's assume the versionId might be "projectID:fileID" for CFP
                string projectId = "";
                string fileId = versionId;
                if (versionId.Contains(':'))
                {
                    var parts = versionId.Split(':');
                    projectId = parts[0];
                    fileId = parts[1];
                }

                string url = $"{ApiBase}/mods/{projectId}/files/{fileId}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", apiKey);

                var httpResponse = await _httpClient.SendAsync(request);
                if (!httpResponse.IsSuccessStatusCode) return null;

                var rootNode = await httpResponse.Content.ReadFromJsonAsync<JsonNode>();
                var fileNode = rootNode?["data"];
                if (fileNode == null) return null;

                var projectInfo = await GetProjectInfoAsync(projectId);
                return MapToMarketplaceVersion(fileNode, projectInfo);
            }
            catch
            {
                return null;
            }
        }

        public async Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId)
        {
            try
            {
                string? apiKey = GetActiveApiKey();
                if (string.IsNullOrEmpty(apiKey)) return null;

                string url = $"{ApiBase}/mods/{projectId}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", apiKey);

                var httpResponse = await _httpClient.SendAsync(request);
                if (!httpResponse.IsSuccessStatusCode) return null;

                var rootNode = await httpResponse.Content.ReadFromJsonAsync<JsonNode>();
                var data = rootNode?["data"];
                if (data == null) return null;

                return new MarketplaceProjectInfo
                {
                    Id = data["id"]?.ToString() ?? "",
                    Title = data["name"]?.ToString() ?? "",
                    Slug = data["slug"]?.ToString() ?? "",
                    IconUrl = data["logo"]?["thumbnailUrl"]?.ToString()
                };
            }
            catch
            {
                return null;
            }
        }

        private MarketplaceVersion MapToMarketplaceVersion(JsonNode fileNode, MarketplaceProjectInfo? project)
        {
            long fileId = long.Parse(fileNode["id"]!.ToString());
            string fileName = fileNode["fileName"]?.ToString() ?? "mod.jar";
            string downloadUrl = fileNode["downloadUrl"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(downloadUrl))
            {
                string part1 = (fileId / 1000).ToString();
                string part2 = (fileId % 1000).ToString("D3");
                downloadUrl = $"https://edge.forgecdn.net/files/{part1}/{part2}/{Uri.EscapeDataString(fileName)}";
            }

            var v = new MarketplaceVersion
            {
                Id = fileId.ToString(),
                Name = fileNode["displayName"]?.ToString() ?? "Latest",
                ProjectId = project?.Id ?? fileNode["modId"]?.ToString() ?? "",
                ProjectTitle = project?.Title ?? "Unknown Project",
                FileName = fileName,
                DownloadUrl = downloadUrl
            };

            var deps = fileNode["dependencies"]?.AsArray();
            if (deps != null)
            {
                foreach (var dep in deps)
                {
                    if (dep == null) continue;
                    int typeInt = dep["relationType"]?.GetValue<int>() ?? 0;
                    DependencyType type = typeInt switch
                    {
                        1 => DependencyType.Embedded,
                        2 => DependencyType.Optional,
                        3 => DependencyType.Required,
                        5 => DependencyType.Incompatible,
                        _ => DependencyType.Optional
                    };

                    v.Dependencies.Add(new MarketplaceDependency
                    {
                        ProjectId = dep["modId"]?.ToString() ?? "",
                        Type = type
                    });
                }
            }

            return v;
        }

        private async Task<(JsonNode File, MarketplaceProjectInfo Project)?> GetLatestVersionWithProjectInfoAsync(string projectId, string mcVersion, string loader)
        {
            try
            {
                if (string.IsNullOrEmpty(projectId)) return null;

                string? apiKey = GetActiveApiKey();
                if (string.IsNullOrEmpty(apiKey)) return null;

                var projectInfo = await GetProjectInfoAsync(projectId);
                if (projectInfo == null) return null;

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

                JsonNode? latestFile = null;
                foreach (var file in files)
                {
                    if (file == null) continue;
                    if (string.IsNullOrWhiteSpace(loader) || FileSupportsLoader(file, loader))
                    {
                        latestFile = file;
                        break;
                    }
                }

                latestFile ??= files[0];
                if (latestFile == null) return null;

                return (latestFile, projectInfo);
            }
            catch
            {
                return null;
            }
        }

        public async Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader)
        {
            var result = await GetLatestVersionWithProjectInfoAsync(projectId, mcVersion, loader);
            if (result == null) return null;

            var mv = MapToMarketplaceVersion(result.Value.File, result.Value.Project);
            return new ModrinthVersion
            {
                Id = mv.Id,
                Name = mv.Name,
                ProjectId = mv.ProjectId,
                Files = new List<ModrinthFile>
                {
                    new ModrinthFile
                    {
                        Url = mv.DownloadUrl,
                        FileName = mv.FileName,
                        IsPrimary = true
                    }
                }
            };
        }
    }
}
