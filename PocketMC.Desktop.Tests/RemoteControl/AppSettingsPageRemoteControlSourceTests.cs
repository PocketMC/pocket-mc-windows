namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class AppSettingsPageRemoteControlSourceTests
{
    [Fact]
    public void AppSettingsPage_ContainsRemoteControlSettingsCardAndWarnings()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "RemoteControl",
            "UI",
            "RemoteControlPage.xaml"));

        Assert.Contains("Remote Control", xaml, StringComparison.Ordinal);
        Assert.Contains("Cloudflare Quick Tunnel creates a temporary public URL.", xaml, StringComparison.Ordinal);
        Assert.Contains("PlayIt HTTPS tunnels require PlayIt Premium. Stop Remote Link disables the dedicated PocketMC Remote Control tunnel.", xaml, StringComparison.Ordinal);
        Assert.Contains("Remote console commands can fully control this Minecraft server.", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleRemoteControlEnabled", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSettingsPage_DelegatesRemoteControlActionsToCoordinator()
    {
        string viewModel = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "ViewModels",
            "RemoteControlSettingsViewModel.cs"));

        Assert.Contains("RemoteControlCoordinator", viewModel, StringComparison.Ordinal);
        Assert.Contains("RestartAllAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("StopAllAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("MapRemoteAccessModeToProviderId", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("cloudflared tunnel --url", viewModel, StringComparison.OrdinalIgnoreCase);
    }
}
