using System.Net;
using System.Net.Http;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;

namespace PocketMC.Desktop.Tests;

public sealed class PoggitServiceTests
{
    [Fact]
    public async Task MissingProviderVersion_DoesNotBecomeFakeOneZeroZero()
    {
        var service = new PoggitService(new MarketplaceTestHttpClientFactory(() =>
            new HttpClient(new MarketplaceDelegateHttpMessageHandler((_, _) => MarketplaceHttpResponses.Json("""
            [
              {
                "name": "ExamplePlugin",
                "artifact_url": "https://poggit.example/ExamplePlugin.phar"
              }
            ]
            """)))));

        MarketplaceVersion? version = await ((IAddonProvider)service).GetLatestVersionAsync("ExamplePlugin", "", "");

        Assert.NotNull(version);
        Assert.Equal("Unknown", version.Name);
        Assert.NotEqual("1.0.0", version.Id);
        Assert.Contains(version.Warnings, warning => warning.Contains("compatibility could not be verified", StringComparison.OrdinalIgnoreCase));
    }
}
