using PocketMC.Desktop;
using PocketMC.Infrastructure.Configuration;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Shell;

public sealed class AppConfigTests
{
    [Fact]
    public void AppVersion_LoadsFromEmbeddedPocketMcConfig()
    {
        var desktopAssembly = typeof(PocketMC.Desktop.App).Assembly;
        using var stream = desktopAssembly.GetManifestResourceStream("PocketMC.Desktop.pocketmc.yml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        var versionMatch = Regex.Match(content, @"(?m)^version:\s*""?([^""\r\n#]+)""?");

        Assert.True(versionMatch.Success);
        Assert.Equal(versionMatch.Groups[1].Value.Trim(), AppConfig.AppVersion);
    }

    [Fact]
    public void AppIdentity_LoadsFromEmbeddedPocketMcConfig()
    {
        Assert.Equal("PocketMC", AppConfig.AppName);
        Assert.Equal("PocketMC", AppConfig.AppTitle);
        Assert.Equal("PocketMC", AppConfig.AppId);
        Assert.Equal("DS Labs", AppConfig.OrganizationName);
    }

    [Fact]
    public void ProviderEndpoints_LoadFromEmbeddedConfig()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderMojangManifest));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderPaperMcApi));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderFabricMeta));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderForgeMeta));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderNeoForgeMaven));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderAdoptiumApi));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderModrinthApi));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderCurseForgeApi));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderPocketmineReleases));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderPhpReleases));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.ProviderPlayitApi));
        Assert.Equal("https://playit.gg", AppConfig.LinkPlayitWebsite);
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.LinkPlayitSetup));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.LinkPlayitAgents));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.HealthCheckPlayit));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.HealthCheckAdoptium));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.HealthCheckModrinth));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.BinaryPlayitDownloadUrl));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.BinaryCloudflaredDownloadUrl));
    }

    [Fact]
    public void ParseYamlContent_UpdatesSocialLinks_AndPreservesLocalVersion()
    {
        string originalVersion = AppConfig.AppVersion;
        string yaml = @"
app_name: ""PocketMC Test""
app_title: ""PocketMC Test Title""
app_id: ""PocketMC.Test""
version: 9.9.9
organization_name: ""DS Labs Test""
organization_tagline: ""Building Test Software""
app_description: ""A test manager description""
link_discord: ""https://discord.gg/custom-test-invite""
link_youtube: ""https://youtube.com/@custom-test""
link_reddit: ""https://reddit.com/r/custom-test""
link_instagram: ""https://instagram.com/custom-test""
link_feedback: ""https://feedback.test.com/form""
link_github: ""https://github.com/custom-test/repo""
link_releases: ""https://github.com/custom-test/repo/releases""
link_website: ""https://test-website.com""
link_docs: ""https://test-docs.com""
link_organization: ""https://test-org.com""
link_donation: ""https://buymeacoffee.com/custom-test""
auth_proxies:
  - ""https://proxy1.test.com""
telemetry_proxies:
  - ""https://telemetry1.test.com""
discord_api_urls:
  - ""https://discord-bot.test.com""
";

        AppConfig.ParseYamlContent(yaml, preserveLocalVersion: true);

        Assert.Equal(originalVersion, AppConfig.AppVersion);
        Assert.Equal("DS Labs Test", AppConfig.OrganizationName);
        Assert.Equal("Building Test Software", AppConfig.OrganizationTagline);
        Assert.Equal("A test manager description", AppConfig.AppDescription);
        Assert.Equal("https://discord.gg/custom-test-invite", AppConfig.LinkDiscord);
        Assert.Equal("https://youtube.com/@custom-test", AppConfig.LinkYouTube);
        Assert.Equal("https://reddit.com/r/custom-test", AppConfig.LinkReddit);
        Assert.Equal("https://instagram.com/custom-test", AppConfig.LinkInstagram);
        Assert.Equal("https://feedback.test.com/form", AppConfig.LinkFeedback);
        Assert.Equal("https://github.com/custom-test/repo", AppConfig.LinkGitHub);
        Assert.Equal("https://github.com/custom-test/repo/releases", AppConfig.LinkReleases);
        Assert.Equal("https://test-website.com", AppConfig.LinkWebsite);
        Assert.Equal("https://test-docs.com", AppConfig.LinkDocs);
        Assert.Equal("https://test-org.com", AppConfig.LinkOrganization);
        Assert.Equal("https://buymeacoffee.com/custom-test", AppConfig.LinkDonation);
        Assert.Contains("https://proxy1.test.com", AppConfig.AuthProxies);
        Assert.Contains("https://telemetry1.test.com", AppConfig.TelemetryProxies);
        Assert.Contains("https://discord-bot.test.com", AppConfig.DiscordApiUrls);

        // Reset to embedded baseline
        AppConfig.LoadEmbeddedConfig();
    }

    [Fact]
    public async Task RefreshRemoteConfigAsync_WhenOnline_UpdatesLinksAndReturnsTrue()
    {
        string mockYaml = @"
link_discord: ""https://discord.gg/new-updated-link""
link_youtube: ""https://youtube.com/@updated""
";
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockYaml)
        });

        using var client = new HttpClient(handler);
        bool result = await AppConfig.RefreshRemoteConfigAsync(client);

        Assert.True(result);
        Assert.Equal("https://discord.gg/new-updated-link", AppConfig.LinkDiscord);
        Assert.Equal("https://youtube.com/@updated", AppConfig.LinkYouTube);

        // Cleanup: reset to embedded baseline
        AppConfig.LoadEmbeddedConfig();
    }

    [Fact]
    public async Task RefreshRemoteConfigAsync_WhenOfflineOrError_FallsBackSilentlyAndReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var client = new HttpClient(handler);
        bool result = await AppConfig.RefreshRemoteConfigAsync(client);

        Assert.False(result);
        // Does not throw and retains existing configuration
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.LinkDiscord));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}