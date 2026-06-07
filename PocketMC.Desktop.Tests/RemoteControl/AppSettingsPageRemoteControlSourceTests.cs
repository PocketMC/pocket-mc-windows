namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class AppSettingsPageRemoteControlSourceTests
{
    [Fact]
    public void AppSettingsPage_ContainsRemoteControlSettingsCardAndWarnings()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml"));

        Assert.Contains("Remote Control", xaml, StringComparison.Ordinal);
        Assert.Contains("Cloudflare Quick Tunnel creates a temporary public URL.", xaml, StringComparison.Ordinal);
        Assert.Contains("Remote console commands can fully control this Minecraft server.", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleRemoteControlEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("BtnPairRemoteDevice", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSettingsPage_DelegatesRemoteControlActionsToCoordinator()
    {
        string codeBehind = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml.cs"));

        Assert.Contains("RemoteControlCoordinator", codeBehind, StringComparison.Ordinal);
        Assert.Contains("StartRemoteLink_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("StopRemoteLink_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PairRemoteDevice_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MapRemoteAccessModeToProviderId", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("cloudflared tunnel --url", codeBehind, StringComparison.OrdinalIgnoreCase);
    }
}
