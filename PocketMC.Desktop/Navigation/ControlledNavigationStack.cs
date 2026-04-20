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
    TunnelCreationGuide,
    PlayitGuide,
    ImageCrop
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

public sealed class ControlledNavigationStack
{
    private readonly List<ControlledNavigationEntry> _entries = new();

    public IReadOnlyList<ControlledNavigationEntry> Entries => _entries;

    public ControlledNavigationEntry? Current => _entries.Count > 0 ? _entries[^1] : null;

    public void Clear() => _entries.Clear();

    public ControlledNavigationEntry Push(
        NavigationRouteKind route,
        NavigationBackTarget backTarget,
        bool clearExistingStack = false)
    {
        if (clearExistingStack)
        {
            _entries.Clear();
        }

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

        if (removed.BackTarget.Kind == NavigationBackTargetKind.PreviousDetail)
        {
            if (_entries.Count == 1)
            {
                // Heal inconsistent state (for example, if the stack was externally mutated)
                // by falling back to the default shell route instead of throwing.
                _entries.Clear();
                return new ControlledBackNavigationResult(
                    true,
                    removed.EntryId,
                    NavigationRouteKind.Dashboard,
                    null,
                    true);
            }

            _entries.RemoveAt(_entries.Count - 1);
            ControlledNavigationEntry target = _entries[^1];
            return new ControlledBackNavigationResult(
                true,
                removed.EntryId,
                target.Route,
                target.EntryId,
                false);
        }

        _entries.RemoveAt(_entries.Count - 1);
        _entries.Clear();
        return new ControlledBackNavigationResult(
            true,
            removed.EntryId,
            removed.BackTarget.Route,
            null,
            true);
    }
}
