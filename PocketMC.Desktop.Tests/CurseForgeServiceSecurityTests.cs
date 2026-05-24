using System.Net;
using System.Net.Http;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class CurseForgeServiceSecurityTests
{
    [Fact]
    public async Task FabricRequest_DoesNotReturnForgeOnlyFile()
    {
        CurseForgeService service = CreateService("""
        [
          {
            "id": 101,
            "modId": 200,
            "displayName": "Forge only",
            "fileName": "forge-only.jar",
            "downloadUrl": "https://cdn.example/forge-only.jar",
            "gameVersions": ["1.20.1", "Forge"],
            "releaseType": 1
          }
        ]
        """);

        MarketplaceVersion? version = await ((IAddonProvider)service)
            .GetLatestVersionAsync("200", "1.20.1", "fabric");

        Assert.Null(version);
    }

    [Fact]
    public async Task ForgeRequest_DoesNotReturnFabricOnlyFile()
    {
        CurseForgeService service = CreateService("""
        [
          {
            "id": 102,
            "modId": 200,
            "displayName": "Fabric only",
            "fileName": "fabric-only.jar",
            "downloadUrl": "https://cdn.example/fabric-only.jar",
            "gameVersions": ["1.20.1", "Fabric"],
            "releaseType": 1
          }
        ]
        """);

        MarketplaceVersion? version = await ((IAddonProvider)service)
            .GetLatestVersionAsync("200", "1.20.1", "forge");

        Assert.Null(version);
    }

    [Fact]
    public async Task EmptyLoader_CanChooseFirstCompatibleFile()
    {
        CurseForgeService service = CreateService("""
        [
          {
            "id": 103,
            "modId": 200,
            "displayName": "Latest",
            "fileName": "latest.jar",
            "downloadUrl": "https://cdn.example/latest.jar",
            "gameVersions": ["1.20.1", "Forge"],
            "releaseType": 1
          }
        ]
        """);

        MarketplaceVersion? version = await ((IAddonProvider)service)
            .GetLatestVersionAsync("200", "1.20.1", "");

        Assert.NotNull(version);
        Assert.Equal("103", version.Id);
        Assert.Equal("latest.jar", version.FileName);
    }

    [Fact]
    public async Task ReleaseFile_IsPreferredOverBetaWhenBothMatch()
    {
        CurseForgeService service = CreateService("""
        [
          {
            "id": 104,
            "modId": 200,
            "displayName": "Beta",
            "fileName": "beta.jar",
            "downloadUrl": "https://cdn.example/beta.jar",
            "gameVersions": ["1.20.1", "Fabric"],
            "releaseType": 2
          },
          {
            "id": 105,
            "modId": 200,
            "displayName": "Release",
            "fileName": "release.jar",
            "downloadUrl": "https://cdn.example/release.jar",
            "gameVersions": ["1.20.1", "Fabric"],
            "releaseType": 1
          }
        ]
        """);

        MarketplaceVersion? version = await ((IAddonProvider)service)
            .GetLatestVersionAsync("200", "1.20.1", "fabric");

        Assert.NotNull(version);
        Assert.Equal("105", version.Id);
        Assert.Equal("release", version.ReleaseType);
    }

    private static CurseForgeService CreateService(string filesJson)
    {
        var appState = new ApplicationState();
        appState.ApplySettings(new AppSettings { CurseForgeApiKey = "test-key" });

        var client = new HttpClient(new MarketplaceDelegateHttpMessageHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/mods/200", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json("""
                { "data": { "id": 200, "name": "Test Project", "slug": "test-project" } }
                """);
            }

            if (path.EndsWith("/mods/200/files", StringComparison.OrdinalIgnoreCase))
            {
                return MarketplaceHttpResponses.Json($$""" { "data": {{filesJson}} } """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.curseforge.com")
        };

        return new CurseForgeService(appState, client);
    }
}
