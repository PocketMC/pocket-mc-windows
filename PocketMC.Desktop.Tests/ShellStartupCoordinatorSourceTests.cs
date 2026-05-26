namespace PocketMC.Desktop.Tests;

public sealed class ShellStartupCoordinatorSourceTests
{
    [Fact]
    public void WindowsStartupLaunch_AlwaysTriggersServerAutoStarts()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "ShellStartupCoordinator.cs"));

        Assert.Contains("AppStartupOptions startupOptions", source);
        // The IsWindowsStartup guard was removed — auto-starts should always fire
        Assert.DoesNotContain("!_startupOptions.IsWindowsStartup", source);
        Assert.DoesNotContain("Skipping server auto-start during Windows startup launch.", source);
        Assert.Contains("TriggerServerAutoStarts()", source);
    }
}
