namespace PocketMC.Desktop.Tests;

public sealed class AppSettingsPageStartupSourceTests
{
    [Fact]
    public void Xaml_DefinesAppBehaviorStartupToggles()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml"));

        Assert.Contains("Start PocketMC with Windows", xaml);
        Assert.Contains("Start minimized to tray", xaml);
        Assert.Contains("Minimize to tray on close", xaml);
        Assert.Contains("x:Name=\"ToggleStartWithWindows\"", xaml);
        Assert.Contains("x:Name=\"ToggleStartMinimizedToTray\"", xaml);
        Assert.Contains("x:Name=\"ToggleMinimizeToTrayOnClose\"", xaml);
    }

    [Fact]
    public void CodeBehind_AppliesStartupServiceAndRevertsToggleOnRegistryFailure()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml.cs"));

        Assert.Contains("WindowsStartupService", source);
        Assert.Contains("_windowsStartupService.Apply(settings)", source);
        Assert.Contains("RevertAppBehaviorToggles", source);
        Assert.Contains("Could not update Windows startup settings", source);
    }

    [Fact]
    public void Xaml_DefinesSleepPreventionToggle()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml"));

        Assert.Contains("Keep this PC awake while servers are running", xaml);
        Assert.Contains("Allows the screen to turn off, but prevents Windows from sleeping so servers and tunnels stay online.", xaml);
        Assert.Contains("x:Name=\"ToggleKeepComputerAwakeWhileServersRunning\"", xaml);
        Assert.Contains("ToggleKeepComputerAwakeWhileServersRunning_Changed", xaml);
    }

    [Fact]
    public void CodeBehind_SavesSleepPreventionSettingAndRefreshesCoordinator()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml.cs"));

        Assert.Contains("ServerSleepPreventionCoordinator", source);
        Assert.Contains("ToggleKeepComputerAwakeWhileServersRunning.IsChecked = _applicationState.Settings.KeepComputerAwakeWhileServersRunning", source);
        Assert.Contains("settings.KeepComputerAwakeWhileServersRunning = ToggleKeepComputerAwakeWhileServersRunning.IsChecked == true", source);
        Assert.Contains("_sleepPreventionCoordinator.Refresh()", source);
    }
}
