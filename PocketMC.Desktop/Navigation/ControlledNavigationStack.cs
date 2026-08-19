using System;
using System.Collections.Generic;

namespace PocketMC.Desktop.Navigation;

public enum NavigationRouteKind
{
    Dashboard,
    Tunnel,
    JavaSetup,
    AppSettings,
    About,
    NewInstance,
    ServerSettings,
    PluginBrowser,
    ServerConsole,
    PlayerManagement,
    ImageCrop,
    PortsMap,
    InstanceImport,
    InstanceExport,
    PlayitNetworkStatus
}

public enum NavigationBackTargetKind
{
    ShellRoute,
    PreviousDetail
}

public sealed record NavigationBackTarget(NavigationBackTargetKind Kind, NavigationRouteKind Route)
{
    public static NavigationBackTarget ShellRoute(NavigationRouteKind route) => new(NavigationBackTargetKind.ShellRoute, route);

    public static NavigationBackTarget PreviousDetail() => new(NavigationBackTargetKind.PreviousDetail, NavigationRouteKind.Dashboard);
}

public sealed record ControlledNavigationEntry(string EntryId, NavigationRouteKind Route, NavigationBackTarget BackTarget);

public sealed record ControlledBackNavigationResult(
    bool Success,
    string? RemovedEntryId,
    NavigationRouteKind TargetRoute,
    string? TargetEntryId,
    bool TargetsShellRoute);

public sealed record ControlledForwardNavigationResult(
    bool Success,
    string? TargetEntryId,
    NavigationRouteKind TargetRoute);

public sealed class ControlledNavigationStack
{
    private readonly List<ControlledNavigationEntry> _entries = new();
    private readonly List<ControlledNavigationEntry> _forwardEntries = new();

    public IReadOnlyList<ControlledNavigationEntry> Entries => _entries;
    public IReadOnlyList<ControlledNavigationEntry> ForwardEntries => _forwardEntries;

    public ControlledNavigationEntry? Current => _entries.Count > 0 ? _entries[^1] : null;
    public bool CanNavigateForward => _forwardEntries.Count > 0;

    public void Clear()
    {
        _entries.Clear();
        _forwardEntries.Clear();
    }

    public ControlledNavigationEntry Push(
        NavigationRouteKind route,
        NavigationBackTarget backTarget,
        bool clearExistingStack = false)
    {
        if (clearExistingStack)
        {
            _entries.Clear();
        }

        _forwardEntries.Clear();

        if (backTarget.Kind == NavigationBackTargetKind.PreviousDetail && _entries.Count == 0)
        {
            throw new InvalidOperationException($"Route {route} requires an existing detail route in the stack.");
        }

        var entry = new ControlledNavigationEntry(Guid.NewGuid().ToString("N"), route, backTarget);
        _entries.Add(entry);
        return entry;
    }

    public ControlledBackNavigationResult NavigateBack()
    {
        if (_entries.Count == 0)
        {
            return new ControlledBackNavigationResult(false, null, NavigationRouteKind.Dashboard, null, true);
        }

        ControlledNavigationEntry removed = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        _forwardEntries.Add(removed);

        if (removed.BackTarget.Kind == NavigationBackTargetKind.PreviousDetail)
        {
            if (_entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Route {removed.Route} expected a previous detail route, but the stack is empty.");
            }

            ControlledNavigationEntry target = _entries[^1];
            return new ControlledBackNavigationResult(
                true,
                removed.EntryId,
                target.Route,
                target.EntryId,
                false);
        }

        return new ControlledBackNavigationResult(
            true,
            removed.EntryId,
            removed.BackTarget.Route,
            null,
            true);
    }

    public ControlledForwardNavigationResult NavigateForward()
    {
        if (_forwardEntries.Count == 0)
        {
            return new ControlledForwardNavigationResult(false, null, NavigationRouteKind.Dashboard);
        }

        ControlledNavigationEntry target = _forwardEntries[^1];
        _forwardEntries.RemoveAt(_forwardEntries.Count - 1);
        _entries.Add(target);

        return new ControlledForwardNavigationResult(
            true,
            target.EntryId,
            target.Route);
    }
}
