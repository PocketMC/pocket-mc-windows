using System;
using System.Collections.Generic;
using System.Linq;
using PocketMC.Infrastructure.Storage;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

public sealed class DownloadMirrorServiceTests
{
    private static DownloadMirrorService Create(
        bool enabled = true,
        string? custom = null,
        params string[] tunnels)
    {
        var options = new DownloadMirrorOptions(enabled, custom, tunnels);
        return new DownloadMirrorService(() => options);
    }

    [Fact]
    public void GetCandidates_WhenDisabled_ReturnsDirectOnly()
    {
        DownloadMirrorService service = Create(false, null, "https://ghfast.top/{url}");

        var candidates = service.GetCandidates(new Uri("https://github.com/a/b/releases/download/v1/server.jar"));

        Assert.Single(candidates);
        Assert.Equal("https://github.com/a/b/releases/download/v1/server.jar", candidates[0].Uri.AbsoluteUri);
        Assert.Null(candidates[0].Tunnel);
    }

    [Fact]
    public void GetCandidates_OrdersCustomThenTunnelsThenDirect()
    {
        DownloadMirrorService service = Create(
            true,
            "https://custom.example/{url}",
            "https://ghfast.top/{url}",
            "https://gh-proxy.com/{url}");

        var candidates = service.GetCandidates(new Uri("https://github.com/a/b/releases/download/v1/server.jar"));

        Assert.Equal(4, candidates.Count);
        Assert.Equal("custom", candidates[0].Tunnel);
        Assert.Equal("https://custom.example/https://github.com/a/b/releases/download/v1/server.jar", candidates[0].Uri.AbsoluteUri);
        Assert.Equal("https://ghfast.top/{url}", candidates[1].Tunnel);
        Assert.Equal("https://gh-proxy.com/{url}", candidates[2].Tunnel);
        Assert.Null(candidates[3].Tunnel);
    }

    [Fact]
    public void GetCandidates_UsesTrailingSlashPrefixTemplate()
    {
        DownloadMirrorService service = Create(true, "https://mirror.example/gh/");

        var candidates = service.GetCandidates(new Uri("https://raw.githubusercontent.com/a/b/main/file.json"));

        Assert.Equal(
            "https://mirror.example/gh/https://raw.githubusercontent.com/a/b/main/file.json",
            candidates[0].Uri.AbsoluteUri);
    }

    [Fact]
    public void GetCandidates_IgnoresInvalidCustomTemplateAndAppliesTunnelsToGitHubOnly()
    {
        DownloadMirrorService service = Create(true, "not a url", "https://ghfast.top/{url}");

        var github = service.GetCandidates(new Uri("https://github.com/a/b/releases/download/v1/server.jar"));
        var paper = service.GetCandidates(new Uri("https://fill.papermc.io/v3/projects/paper"));

        Assert.Equal(2, github.Count);
        Assert.Equal("https://ghfast.top/{url}", github[0].Tunnel);
        Assert.Single(paper);
    }

    [Fact]
    public void GetCandidates_MapsMojangHostsToBmclApi()
    {
        DownloadMirrorService service = Create(true, null, "https://ghfast.top/{url}");

        var manifest = service.GetCandidates(new Uri("https://launchermeta.mojang.com/mc/game/version_manifest.json"));
        var meta = service.GetCandidates(new Uri("https://piston-meta.mojang.com/v1/packages/abc123/1.20.1.json"));
        var library = service.GetCandidates(new Uri("https://libraries.minecraft.net/com/example/lib.jar"));

        Assert.Equal("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json", manifest[0].Uri.AbsoluteUri);
        Assert.Equal("https://bmclapi2.bangbang93.com/version/1.20.1/json", meta[0].Uri.AbsoluteUri);
        Assert.Equal("https://bmclapi2.bangbang93.com/com/example/lib.jar", library[0].Uri.AbsoluteUri);
        Assert.Null(manifest[^1].Tunnel);
    }

    [Fact]
    public void GetCandidates_MapsRawFilesToJsDelivr()
    {
        DownloadMirrorService service = Create(true, null, "https://ghfast.top/{url}");

        var candidates = service.GetCandidates(new Uri("https://raw.githubusercontent.com/owner/repo/main/path/server.jar"));

        Assert.Equal("jsdelivr", candidates[0].Tunnel);
        Assert.Equal("https://cdn.jsdelivr.net/gh/owner/repo@main/path/server.jar", candidates[0].Uri.AbsoluteUri);
    }

    [Fact]
    public void GetCandidates_LeavesLoopbackAndIpHostsAlone()
    {
        DownloadMirrorService service = Create(true, "https://custom.example/{url}", "https://ghfast.top/{url}");

        Assert.Single(service.GetCandidates(new Uri("http://127.0.0.1:8080/file.jar")));
        Assert.Single(service.GetCandidates(new Uri("http://localhost/file.jar")));
        Assert.Single(service.GetCandidates(new Uri("http://box.local/file.jar")));
    }

    [Fact]
    public void ReportFailure_SinksTunnelForCooldown()
    {
        var options = new DownloadMirrorOptions(true, null, new[] { "https://ghfast.top/{url}", "https://gh-proxy.com/{url}" });
        var service = new DownloadMirrorService(() => options);
        var url = new Uri("https://github.com/a/b/releases/download/v1/server.jar");

        service.ReportFailure("https://ghfast.top/{url}");
        var candidates = service.GetCandidates(url);

        Assert.Equal("https://gh-proxy.com/{url}", candidates[0].Tunnel);
        Assert.Equal("https://ghfast.top/{url}", candidates[1].Tunnel);

        service.ReportSuccess("https://ghfast.top/{url}");
        Assert.Equal("https://ghfast.top/{url}", service.GetCandidates(url)[0].Tunnel);
    }

    [Theory]
    [InlineData("https://ghfast.top/{url}", true)]
    [InlineData("https://mirror.example/gh/", true)]
    [InlineData("", false)]
    [InlineData("ftp://mirror/{url}", false)]
    [InlineData("https://mirror.example/plain", false)]
    public void IsValidTemplate_ValidatesPlaceholderOrPrefix(string template, bool expected)
    {
        Assert.Equal(expected, DownloadMirrorService.IsValidTemplate(template));
    }
}
