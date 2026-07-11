namespace PocketMC.Desktop.Tests;

public sealed class ShellStartupCoordinatorSourceTests
{
    [Fact]
    public void StartupFailure_WritesConcreteLogFileForUserVisibleDialog()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "ShellStartupCoordinator.cs"));

        Assert.Contains("WriteStartupFailureLog(ex)", source);
        Assert.Contains("startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log", source);
        Assert.Contains("Details were written to:", source);
    }

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
