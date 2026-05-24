using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;

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

    private static ModrinthService CreateService(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        return new ModrinthService(new HttpClient(new MarketplaceDelegateHttpMessageHandler(responder)));
    }
}
