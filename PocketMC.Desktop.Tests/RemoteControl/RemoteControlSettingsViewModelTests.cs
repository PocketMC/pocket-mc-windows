using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.Setup.ViewModels;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteControlSettingsViewModelTests
{
    [Theory]
    [InlineData(RemoteAccessMode.LanOnly, "none")]
    [InlineData(RemoteAccessMode.CloudflaredQuickTunnel, "cloudflared-quick")]
    [InlineData(RemoteAccessMode.PlayitHttpsTunnel, "playit-https")]
    public void MapRemoteAccessModeToProviderId_ReturnsProviderId(RemoteAccessMode mode, string expectedProviderId)
    {
        Assert.Equal(expectedProviderId, RemoteControlSettingsViewModel.MapRemoteAccessModeToProviderId(mode));
    }

    [Fact]
    public void AccessModeOptions_ExposeOnlySupportedModesWithFriendlyLabels()
    {
        var options = RemoteControlSettingsViewModel.RemoteAccessModeOptions;

        Assert.Collection(
            options,
            option =>
            {
                Assert.Equal(RemoteAccessMode.CloudflaredQuickTunnel, option.Mode);
                Assert.Equal("Cloudflare Quick Tunnel", option.Label);
            },
            option =>
            {
                Assert.Equal(RemoteAccessMode.PlayitHttpsTunnel, option.Mode);
                Assert.Equal("PlayIt Premium HTTPS", option.Label);
            });
    }
}
