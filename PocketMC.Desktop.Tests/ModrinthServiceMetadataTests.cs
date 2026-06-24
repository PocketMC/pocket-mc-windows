using PocketMC.Desktop.Features.Marketplace.Models;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class ModrinthServiceMetadataTests
{
    [Fact]
    public async Task VersionMapping_PreservesPreferredHashAndReleaseType()
    {
        ModrinthService service = CreateService((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/version/v1", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json("""
                {
                  "id": "v1",
                  "project_id": "project-1",
                  "name": "Release 1",
                  "version_type": "release",
                  "files": [
                    {
                      "url": "https://cdn.example/mod.jar",
                      "filename": "mod.jar",
                      "primary": true,
                      "hashes": {
                        "sha1": "1111111111111111111111111111111111111111",
                        "sha512": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                      }
                    }
                  ]
                }
                """);
            }

            if (path.EndsWith("/project/project-1", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "project-1", "slug": "project-one", "title": "Project One" }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        MarketplaceVersion? version = await service.GetVersionByIdAsync("v1");

        Assert.NotNull(version);
        Assert.Equal("sha512", version.HashType);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", version.Hash);
        Assert.Equal("release", version.ReleaseType);
    }

    [Fact]
    public async Task GetVersionByHashAsync_SelectsFileMatchingManifestHash()
    {
        const string matchingHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string otherHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        ModrinthService service = CreateService((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/version_files", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json($$"""
                {
                  "{{matchingHash}}": {
                    "id": "v1",
                    "project_id": "project-1",
                    "name": "Release 1",
                    "version_type": "release",
                    "loaders": ["fabric"],
                    "files": [
                      {
                        "url": "https://cdn.example/wrong.jar",
                        "filename": "wrong.jar",
                        "primary": true,
                        "hashes": { "sha512": "{{otherHash}}" }
                      },
                      {
                        "url": "https://cdn.example/right.jar",
                        "filename": "right.jar",
                        "primary": false,
                        "hashes": { "sha512": "{{matchingHash}}" }
                      }
                    ]
                  }
                }
                """);
            }

            if (path.EndsWith("/project/project-1", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "project-1", "slug": "project-one", "title": "Project One" }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        MarketplaceVersion? version = await service.GetVersionByHashAsync(matchingHash, "sha512", new[] { "fabric" });

        Assert.NotNull(version);
        Assert.Equal("right.jar", version.FileName);
        Assert.Equal("https://cdn.example/right.jar", version.DownloadUrl);
        Assert.Equal(matchingHash, version.Hash);
        Assert.Equal("fabric", version.SelectedLoader);
    }

    [Fact]
    public async Task LatestVersion_PrefersReleaseOverBeta()
    {
        ModrinthService service = CreateService((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/project/project-slug/version", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json("""
                [
                  {
                    "id": "beta-version",
                    "project_id": "project-1",
                    "name": "Beta",
                    "version_type": "beta",
                    "loaders": ["fabric"],
                    "files": [{ "url": "https://cdn.example/beta.jar", "filename": "beta.jar", "primary": true }]
                  },
                  {
                    "id": "release-version",
                    "project_id": "project-1",
                    "name": "Release",
                    "version_type": "release",
                    "loaders": ["fabric"],
                    "files": [{ "url": "https://cdn.example/release.jar", "filename": "release.jar", "primary": true }]
                  }
                ]
                """);
            }

            if (path.EndsWith("/project/project-1", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "project-1", "slug": "project-slug", "title": "Project One" }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        MarketplaceVersion? version = await ((IAddonProvider)service)
            .GetLatestVersionAsync("project-slug", "1.20.1", "fabric");

        Assert.NotNull(version);
        Assert.Equal("release-version", version.Id);
        Assert.Equal("release", version.ReleaseType);
    }

    [Fact]
    public async Task GetLatestVersionAsync_ForPaper_TriesPaperSpigotBukkitInOrder()
    {
        var queriedUrls = new List<string>();
        ModrinthService service = CreateService((request, _) =>
        {
            string url = request.RequestUri?.ToString() ?? "";
            queriedUrls.Add(url);

            if (url.Contains("/version"))
            {
                if (url.Contains("bukkit"))
                {
                    return MarketplaceHttpResponses.Json("""
                    [
                      {
                        "id": "bukkit-version",
                        "project_id": "proj-1",
                        "name": "Bukkit Version",
                        "version_type": "release",
                        "loaders": ["bukkit"],
                        "files": [{ "url": "https://cdn.example/bukkit.jar", "filename": "bukkit.jar", "primary": true }]
                      }
                    ]
                    """);
                }
                return MarketplaceHttpResponses.Json("[]");
            }
            if (url.Contains("/project/test-project"))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "proj-1", "slug": "test-project", "title": "Test Project" }""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var version = await ((IAddonProvider)service).GetLatestVersionAsync("test-project", "1.21.1", new[] { "paper", "spigot", "bukkit" });

        Assert.NotNull(version);
        Assert.Equal("bukkit-version", version.Id);
        var versionQueries = queriedUrls.Where(u => u.Contains("/version")).ToList();
        Assert.Equal(5, versionQueries.Count);
        Assert.Contains("paper", versionQueries[0].ToLowerInvariant());
        Assert.Contains("spigot", versionQueries[2].ToLowerInvariant());
        Assert.Contains("bukkit", versionQueries[4].ToLowerInvariant());
    }

    [Fact]
    public async Task GetLatestVersionAsync_ForSpigot_TriesSpigotBukkitInOrder()
    {
        var queriedUrls = new List<string>();
        ModrinthService service = CreateService((request, _) =>
        {
            string url = request.RequestUri?.ToString() ?? "";
            queriedUrls.Add(url);

            if (url.Contains("/version"))
            {
                if (url.Contains("bukkit"))
                {
                    return MarketplaceHttpResponses.Json("""
                    [
                      {
                        "id": "bukkit-version",
                        "project_id": "proj-1",
                        "name": "Bukkit Version",
                        "version_type": "release",
                        "loaders": ["bukkit"],
                        "files": [{ "url": "https://cdn.example/bukkit.jar", "filename": "bukkit.jar", "primary": true }]
                      }
                    ]
                    """);
                }
                return MarketplaceHttpResponses.Json("[]");
            }
            if (url.Contains("/project/test-project"))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "proj-1", "slug": "test-project", "title": "Test Project" }""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var version = await ((IAddonProvider)service).GetLatestVersionAsync("test-project", "1.21.1", new[] { "spigot", "bukkit" });

        Assert.NotNull(version);
        Assert.Equal("bukkit-version", version.Id);
        var versionQueries = queriedUrls.Where(u => u.Contains("/version")).ToList();
        Assert.Equal(3, versionQueries.Count);
        Assert.Contains("spigot", versionQueries[0].ToLowerInvariant());
        Assert.Contains("bukkit", versionQueries[2].ToLowerInvariant());
    }

    [Fact]
    public async Task GetLatestVersionAsync_ExactVersionAndPaperMatch_Works()
    {
        ModrinthService service = CreateService((request, _) =>
        {
            string url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("/version") && url.Contains("1.21.1") && url.Contains("paper"))
            {
                return MarketplaceHttpResponses.Json("""
                [
                  {
                    "id": "paper-1.21.1-version",
                    "project_id": "proj-1",
                    "name": "Paper 1.21.1 Version",
                    "version_type": "release",
                    "loaders": ["paper"],
                    "files": [{ "url": "https://cdn.example/paper.jar", "filename": "paper.jar", "primary": true }]
                  }
                ]
                """);
            }
            if (url.Contains("/project/test-project"))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "proj-1", "slug": "test-project", "title": "Test Project" }""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var version = await ((IAddonProvider)service).GetLatestVersionAsync("test-project", "1.21.1", new[] { "paper" });

        Assert.NotNull(version);
        Assert.Equal("paper-1.21.1-version", version.Id);
    }

    [Fact]
    public async Task GetLatestVersionAsync_ExactVersionFails_FallbackWorks()
    {
        ModrinthService service = CreateService((request, _) =>
        {
            string url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("/version"))
            {
                if (url.Contains("1.21") && !url.Contains("1.21.1") && url.Contains("paper"))
                {
                    return MarketplaceHttpResponses.Json("""
                    [
                      {
                        "id": "paper-1.21-version",
                        "project_id": "proj-1",
                        "name": "Paper 1.21 Version",
                        "version_type": "release",
                        "loaders": ["paper"],
                        "files": [{ "url": "https://cdn.example/paper.jar", "filename": "paper.jar", "primary": true }]
                      }
                    ]
                    """);
                }
                return MarketplaceHttpResponses.Json("[]");
            }
            if (url.Contains("/project/test-project"))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "proj-1", "slug": "test-project", "title": "Test Project" }""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var version = await ((IAddonProvider)service).GetLatestVersionAsync("test-project", "1.21.1", new[] { "paper" });

        Assert.NotNull(version);
        Assert.Equal("paper-1.21-version", version.Id);
    }

    [Fact]
    public void SelectCompatibleFile_DoesNotSelectFabricFileForPaper()
    {
        var modVersion = new ModrinthVersion
        {
            Files = new List<ModrinthFile>
            {
                new ModrinthFile { FileName = "mod-fabric.jar", Url = "https://example.com/fabric.jar", IsPrimary = false },
                new ModrinthFile { FileName = "mod-paper.jar", Url = "https://example.com/paper.jar", IsPrimary = true }
            }
        };

        var selected = ModrinthService.SelectCompatibleFile(modVersion, "paper");
        Assert.NotNull(selected);
        Assert.Equal("mod-paper.jar", selected.FileName);
    }

    [Fact]
    public async Task GetLatestVersionAsync_NonReleaseFallback_AddsWarning()
    {
        ModrinthService service = CreateService((request, _) =>
        {
            string url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("/project/test-project/version"))
            {
                return MarketplaceHttpResponses.Json("""
                [
                  {
                    "id": "beta-version",
                    "project_id": "proj-1",
                    "name": "Beta Version",
                    "version_type": "beta",
                    "loaders": ["paper"],
                    "files": [{ "url": "https://cdn.example/beta.jar", "filename": "beta.jar", "primary": true }]
                  }
                ]
                """);
            }
            if (url.Contains("/project/test-project"))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "proj-1", "slug": "test-project", "title": "Test Project" }""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var version = await ((IAddonProvider)service).GetLatestVersionAsync("test-project", "1.21.1", new[] { "paper" });

        Assert.NotNull(version);
        Assert.Equal("beta-version", version.Id);
        Assert.NotEmpty(version.Warnings);
        Assert.Contains("beta", version.Warnings[0]);
    }

    [Fact]
    public async Task GetLatestVersionAsync_NoCompatibleLoader_ReturnsNull()
    {
        ModrinthService service = CreateService((request, _) =>
        {
            string url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("/project/test-project/version"))
            {
                return MarketplaceHttpResponses.Json("[]");
            }
            if (url.Contains("/project/test-project"))
            {
                return MarketplaceHttpResponses.Json("""{ "id": "proj-1", "slug": "test-project", "title": "Test Project" }""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var version = await ((IAddonProvider)service).GetLatestVersionAsync("test-project", "1.21.1", new[] { "paper" });

        Assert.Null(version);
    }

    private static ModrinthService CreateService(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        return new ModrinthService(new HttpClient(new MarketplaceDelegateHttpMessageHandler(responder)));
    }
}



