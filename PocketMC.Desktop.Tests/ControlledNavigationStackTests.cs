using System;
using PocketMC.Desktop.Navigation;

namespace PocketMC.Desktop.Tests;

public class ControlledNavigationStackTests
{
    [Fact]
    public void ServerSettingsBackRoutesToDashboard()
    {
        var stack = new ControlledNavigationStack();
        stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.True(result.Success);
        Assert.True(result.TargetsShellRoute);
        Assert.Equal(NavigationRouteKind.Dashboard, result.TargetRoute);
        Assert.Empty(stack.Entries);
    }

    [Fact]
    public void PluginMarketplaceBackReturnsToSameServerSettingsEntry()
    {
        var stack = new ControlledNavigationStack();
        ControlledNavigationEntry settings = stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));
        ControlledNavigationEntry browser = stack.Push(
            NavigationRouteKind.PluginBrowser,
            NavigationBackTarget.PreviousDetail());

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.True(result.Success);
        Assert.False(result.TargetsShellRoute);
        Assert.Equal(NavigationRouteKind.ServerSettings, result.TargetRoute);
        Assert.Equal(settings.EntryId, result.TargetEntryId);
        Assert.Equal(browser.EntryId, result.RemovedEntryId);
        Assert.Equal(settings.EntryId, stack.Current?.EntryId);
    }

    [Fact]
    public void ModsMarketplaceBackReturnsToSameServerSettingsEntry()
    {
        var stack = new ControlledNavigationStack();
        ControlledNavigationEntry settings = stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));
        stack.Push(
            NavigationRouteKind.PluginBrowser,
            NavigationBackTarget.PreviousDetail());

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.Equal(NavigationRouteKind.ServerSettings, result.TargetRoute);
        Assert.Equal(settings.EntryId, result.TargetEntryId);
        Assert.Single(stack.Entries);
    }

    [Fact]
    public void ModpacksMarketplaceBackReturnsToSameServerSettingsEntry()
    {
        var stack = new ControlledNavigationStack();
        ControlledNavigationEntry settings = stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));
        stack.Push(
            NavigationRouteKind.PluginBrowser,
            NavigationBackTarget.PreviousDetail());

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.Equal(NavigationRouteKind.ServerSettings, result.TargetRoute);
        Assert.Equal(settings.EntryId, result.TargetEntryId);
        Assert.Equal(settings.EntryId, stack.Current?.EntryId);
    }

    [Fact]
    public void ServerConsoleBackRoutesToDashboardAndClearsIntermediates()
    {
        var stack = new ControlledNavigationStack();
        stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));
        stack.Push(
            NavigationRouteKind.PluginBrowser,
            NavigationBackTarget.PreviousDetail());
        stack.Push(
            NavigationRouteKind.ServerConsole,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard),
            clearExistingStack: true);

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.True(result.Success);
        Assert.True(result.TargetsShellRoute);
        Assert.Equal(NavigationRouteKind.Dashboard, result.TargetRoute);
        Assert.Empty(stack.Entries);
    }

    [Fact]
    public void TunnelCreationGuideBackRoutesToDashboard()
    {
        var stack = new ControlledNavigationStack();
        stack.Push(
            NavigationRouteKind.TunnelCreationGuide,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard),
            clearExistingStack: true);

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.True(result.Success);
        Assert.True(result.TargetsShellRoute);
        Assert.Equal(NavigationRouteKind.Dashboard, result.TargetRoute);
    }

    [Fact]
    public void PlayitGuideBackRoutesToTunnel()
    {
        var stack = new ControlledNavigationStack();
        stack.Push(
            NavigationRouteKind.TunnelCreationGuide,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Tunnel),
            clearExistingStack: true);

        ControlledBackNavigationResult result = stack.NavigateBack();

        Assert.True(result.Success);
        Assert.True(result.TargetsShellRoute);
        Assert.Equal(NavigationRouteKind.Tunnel, result.TargetRoute);
    }

    [Fact]
    public void OpeningMarketplaceWithoutParentThrows()
    {
        var stack = new ControlledNavigationStack();

        Assert.Throws<InvalidOperationException>(() =>
            stack.Push(
                NavigationRouteKind.PluginBrowser,
                NavigationBackTarget.PreviousDetail()));
    }

    [Fact]
    public void RepeatedBackPressesStopAtEmptyStack()
    {
        var stack = new ControlledNavigationStack();
        stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));

        ControlledBackNavigationResult first = stack.NavigateBack();
        ControlledBackNavigationResult second = stack.NavigateBack();

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Empty(stack.Entries);
    }
}
