using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.Features.Shell;

public sealed class MainWindowCloseBehaviorTests
{
    [Fact]
    public void Decide_HidesToTrayWhenServersAreRunning()
    {
        MainWindowCloseAction action = MainWindowCloseBehavior.Decide(
            explicitExitRequested: false,
            hasRunningServers: true,
            minimizeToTrayOnClose: false,
            isRemoteControlRunning: false);

        Assert.Equal(MainWindowCloseAction.HideToTray, action);
    }

    [Fact]
    public void Decide_HidesToTrayWhenSettingIsEnabled()
    {
        MainWindowCloseAction action = MainWindowCloseBehavior.Decide(
            explicitExitRequested: false,
            hasRunningServers: false,
            minimizeToTrayOnClose: true,
            isRemoteControlRunning: false);

        Assert.Equal(MainWindowCloseAction.HideToTray, action);
    }

    [Fact]
    public void Decide_AllowsExitWhenExplicitExitIsRequested()
    {
        MainWindowCloseAction action = MainWindowCloseBehavior.Decide(
            explicitExitRequested: true,
            hasRunningServers: true,
            minimizeToTrayOnClose: true,
            isRemoteControlRunning: true);

        Assert.Equal(MainWindowCloseAction.Exit, action);
    }

    [Fact]
    public void Decide_AllowsExitWhenNoTrayConditionApplies()
    {
        MainWindowCloseAction action = MainWindowCloseBehavior.Decide(
            explicitExitRequested: false,
            hasRunningServers: false,
            minimizeToTrayOnClose: false,
            isRemoteControlRunning: false);

        Assert.Equal(MainWindowCloseAction.Exit, action);
    }
    [Fact]
    public void Decide_HidesToTrayWhenRemoteControlIsRunning()
    {
        MainWindowCloseAction action = MainWindowCloseBehavior.Decide(
            explicitExitRequested: false,
            hasRunningServers: false,
            minimizeToTrayOnClose: false,
            isRemoteControlRunning: true);

        Assert.Equal(MainWindowCloseAction.HideToTray, action);
    }
}
