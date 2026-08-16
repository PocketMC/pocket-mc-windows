using PocketMC.Domain.Models;

namespace PocketMC.Domain.Tests.Models;

public sealed class AppStartupOptionsTests
{
    [Fact]
    public void Parse_DetectsWindowsStartupAndMinimizedSwitches()
    {
        AppStartupOptions options = AppStartupOptions.Parse(new[] { "--windows-startup", "--minimized" });

        Assert.True(options.IsWindowsStartup);
        Assert.True(options.IsMinimized);
        Assert.True(options.ShouldStartMinimizedToTray);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        AppStartupOptions options = AppStartupOptions.Parse(new[] { "--Windows-Startup", "--MINIMIZED" });

        Assert.True(options.IsWindowsStartup);
        Assert.True(options.IsMinimized);
    }

    [Fact]
    public void Parse_RequiresBothSwitchesForTrayStartup()
    {
        AppStartupOptions options = AppStartupOptions.Parse(new[] { "--minimized" });

        Assert.False(options.IsWindowsStartup);
        Assert.True(options.IsMinimized);
        Assert.False(options.ShouldStartMinimizedToTray);
    }

    [Fact]
    public void Parse_DetectsCustomUriScheme()
    {
        AppStartupOptions options = AppStartupOptions.Parse(new[] { "pocketmc://associate-discord?userId=123" });

        Assert.Equal("pocketmc://associate-discord?userId=123", options.ActivatedUri);
    }
}