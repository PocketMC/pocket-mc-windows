using PocketMC.RemoteControl.Tunnels;

namespace PocketMC.Desktop.Tests.RemoteControl;

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

    [Fact]
    public void TryParsePublicUrl_IgnoresNonTryCloudflareUrls()
    {
        Assert.False(CloudflaredQuickTunnelProvider.TryParsePublicUrl("https://example.com", out string? url));
        Assert.Null(url);
    }
}
