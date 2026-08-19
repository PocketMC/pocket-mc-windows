using PocketMC.Domain.Models;
using PocketMC.RemoteControl.Tunnels;

namespace PocketMC.RemoteControl.Tests.Tunnels;

public sealed class CloudflaredQuickTunnelProviderTests
{
    [Theory]
    [InlineData("2026-06-06 INF Requesting new quick Tunnel on trycloudflare.com... https://gentle-river-42.trycloudflare.com", "https://gentle-river-42.trycloudflare.com")]
    [InlineData("Tunnel ready at HTTPS://LOUD-NAME.trycloudflare.com", "HTTPS://LOUD-NAME.trycloudflare.com")]
    public void TryParsePublicUrl_ExtractsTryCloudflareUrl(string line, string expected)
    {
        Assert.True(CloudflaredQuickTunnelProvider.TryParsePublicUrl(line, out string? url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("ERR failed to connect to https://api.trycloudflare.com")]
    public void TryParsePublicUrl_IgnoresNonTryCloudflareUrls(string line)
    {
        Assert.False(CloudflaredQuickTunnelProvider.TryParsePublicUrl(line, out string? url));
        Assert.Null(url);
    }
}