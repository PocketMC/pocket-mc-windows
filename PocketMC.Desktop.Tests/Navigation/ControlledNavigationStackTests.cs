using System;
using PocketMC.Desktop.Navigation;

namespace PocketMC.Desktop.Tests.Navigation;

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

    [Fact]
    public void NavigateBackAndForward_RestoresPreviousDetailRoute()
    {
        var stack = new ControlledNavigationStack();
        ControlledNavigationEntry settings = stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));

        Assert.False(stack.CanNavigateForward);

        ControlledBackNavigationResult backResult = stack.NavigateBack();
        Assert.True(backResult.Success);
        Assert.Empty(stack.Entries);
        Assert.True(stack.CanNavigateForward);
        Assert.Single(stack.ForwardEntries);

        ControlledForwardNavigationResult forwardResult = stack.NavigateForward();
        Assert.True(forwardResult.Success);
        Assert.Equal(settings.EntryId, forwardResult.TargetEntryId);
        Assert.Equal(NavigationRouteKind.ServerSettings, forwardResult.TargetRoute);
        Assert.Single(stack.Entries);
        Assert.False(stack.CanNavigateForward);
    }

    [Fact]
    public void PushingNewRouteClearsForwardStack()
    {
        var stack = new ControlledNavigationStack();
        stack.Push(
            NavigationRouteKind.ServerSettings,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));

        stack.NavigateBack();
        Assert.True(stack.CanNavigateForward);

        stack.Push(
            NavigationRouteKind.ServerConsole,
            NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard));

        Assert.False(stack.CanNavigateForward);
        Assert.Empty(stack.ForwardEntries);
    }
}
