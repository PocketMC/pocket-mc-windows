using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Navigation;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.InstanceCreation;

namespace PocketMC.Desktop.Infrastructure
{
    public class AppNavigationService : IAppNavigationService
    {
        private readonly ControlledNavigationStack _detailStack = new();
        private readonly List<DetailPageEntry> _detailPages = new();
        private readonly IShellUIStateService _uiStateService;
        private IShellHost? _shellHost;

        public AppNavigationService(IShellUIStateService uiStateService)
        {
            _uiStateService = uiStateService;
        }

        public void Initialize(IShellHost shellHost)
        {
            _shellHost = shellHost;
        }

        public bool NavigateToDashboard()
        {
            if (_shellHost == null) return false;

            bool navigated = _shellHost.ShowShellPage(typeof(DashboardPage));
            if (navigated)
            {
                ClearDetailStack();
                _uiStateService.UpdateBreadcrumb("Dashboard");
            }

            return navigated;
        }

        public bool NavigateToTunnel()
        {
            if (_shellHost == null) return false;

            bool navigated = _shellHost.ShowShellPage(typeof(TunnelPage));
            if (navigated)
            {
                ClearDetailStack();
                _uiStateService.UpdateBreadcrumb("Tunnel");
            }

            return navigated;
        }

        public bool NavigateToShellPage(Type pageType)
        {
            if (_shellHost == null) return false;

            bool navigated = _shellHost.ShowShellPage(pageType);
            if (navigated)
            {
                ClearDetailStack();
                _uiStateService.UpdateBreadcrumb(GetBreadcrumbForPageType(pageType));
            }

            return navigated;
        }

        public bool NavigateToDetailPage(
            Page page,
            string breadcrumbLabel,
            DetailRouteKind routeKind,
            DetailBackNavigation backNavigation,
            bool clearDetailStack = false)
        {
            if (_shellHost == null) return false;

            ValidateDetailTransition(routeKind, backNavigation);

            bool navigated = _shellHost.ShowDetailPage(page, breadcrumbLabel);
            if (!navigated) return false;

            if (clearDetailStack) ClearDetailStack();

            ControlledNavigationEntry stackEntry = _detailStack.Push(
                MapRoute(routeKind),
                MapBackTarget(backNavigation),
                clearExistingStack: false);

            _detailPages.Add(new DetailPageEntry(stackEntry.EntryId, routeKind, page, breadcrumbLabel));
            _uiStateService.UpdateBreadcrumb(breadcrumbLabel);

            return true;
        }

        public bool NavigateBack()
        {
            if (_shellHost == null) return false;

            ControlledBackNavigationResult result = _detailStack.NavigateBack();
            if (!result.Success) return false;

            RemoveDetailEntry(result.RemovedEntryId, dispose: true);

            if (result.TargetsShellRoute)
            {
                var pageType = MapShellPageType(result.TargetRoute);
                bool navigated = _shellHost.ShowShellPage(pageType);
                if (navigated) _uiStateService.UpdateBreadcrumb(GetBreadcrumbForPageType(pageType));
                return navigated;
            }

            DetailPageEntry? targetEntry = _detailPages.LastOrDefault(entry => entry.EntryId == result.TargetEntryId);
            if (targetEntry == null) return NavigateToDashboard();

            bool detailNavigated = _shellHost.ShowDetailPage(targetEntry.Page, targetEntry.BreadcrumbLabel);
            if (detailNavigated) _uiStateService.UpdateBreadcrumb(targetEntry.BreadcrumbLabel);
            return detailNavigated;
        }

        private string? GetBreadcrumbForPageType(Type pageType)
        {
            return pageType.Name switch
            {
                nameof(DashboardPage) => "Dashboard",
                nameof(TunnelPage) => "Tunnel",
                nameof(JavaSetupPage) => "Java Setup",
                nameof(AboutPage) => "About",
                nameof(AppSettingsPage) => "Settings",
                nameof(NewInstancePage) => "New Instance",
                nameof(ServerSettingsPage) => "Server Settings",
                nameof(ServerConsolePage) => "Console",
                _ => null
            };
        }

        private void ValidateDetailTransition(DetailRouteKind routeKind, DetailBackNavigation backNavigation)
        {
            if (backNavigation != DetailBackNavigation.PreviousDetail)
            {
                return;
            }

            DetailPageEntry? current = _detailPages.LastOrDefault();
            if (current == null)
            {
                throw new InvalidOperationException($"{routeKind} requires a parent detail route, but none is active.");
            }

            bool validParent = routeKind switch
            {
                DetailRouteKind.PluginBrowser => current.RouteKind == DetailRouteKind.ServerSettings,
                DetailRouteKind.ImageCrop => current.RouteKind == DetailRouteKind.ServerSettings,
                _ => true
            };

            if (!validParent)
            {
                throw new InvalidOperationException(
                    $"{routeKind} cannot be opened from {current.RouteKind}. The current flow is not allowed.");
            }
        }

        private void ClearDetailStack()
        {
            foreach (DetailPageEntry entry in _detailPages)
            {
                DisposePage(entry.Page);
            }

            _detailPages.Clear();
            _detailStack.Clear();
        }

        private DetailPageEntry? RemoveDetailEntry(string? entryId, bool dispose)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                return null;
            }

            int index = _detailPages.FindIndex(entry => entry.EntryId == entryId);
            if (index < 0)
            {
                return null;
            }

            DetailPageEntry entry = _detailPages[index];
            _detailPages.RemoveAt(index);

            if (dispose)
            {
                DisposePage(entry.Page);
            }

            return entry;
        }

        private static void DisposePage(Page page)
        {
            if (page is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (page.DataContext is IDisposable dataContextDisposable)
            {
                dataContextDisposable.Dispose();
            }
        }

        private static NavigationRouteKind MapRoute(DetailRouteKind routeKind) => routeKind switch
        {
            DetailRouteKind.NewInstance => NavigationRouteKind.NewInstance,
            DetailRouteKind.ServerSettings => NavigationRouteKind.ServerSettings,
            DetailRouteKind.PluginBrowser => NavigationRouteKind.PluginBrowser,
            DetailRouteKind.ServerConsole => NavigationRouteKind.ServerConsole,
            DetailRouteKind.TunnelCreationGuide => NavigationRouteKind.TunnelCreationGuide,
            DetailRouteKind.PlayitGuide => NavigationRouteKind.PlayitGuide,
            DetailRouteKind.ImageCrop => NavigationRouteKind.ImageCrop,
            _ => throw new ArgumentOutOfRangeException(nameof(routeKind), routeKind, null)
        };

        private static NavigationBackTarget MapBackTarget(DetailBackNavigation backNavigation) => backNavigation switch
        {
            DetailBackNavigation.Dashboard => NavigationBackTarget.ShellRoute(NavigationRouteKind.Dashboard),
            DetailBackNavigation.Tunnel => NavigationBackTarget.ShellRoute(NavigationRouteKind.Tunnel),
            DetailBackNavigation.PreviousDetail => NavigationBackTarget.PreviousDetail(),
            _ => throw new ArgumentOutOfRangeException(nameof(backNavigation), backNavigation, null)
        };

        private static Type MapShellPageType(NavigationRouteKind routeKind) => routeKind switch
        {
            NavigationRouteKind.Dashboard => typeof(DashboardPage),
            NavigationRouteKind.Tunnel => typeof(TunnelPage),
            NavigationRouteKind.JavaSetup => typeof(JavaSetupPage),
            NavigationRouteKind.AppSettings => typeof(AppSettingsPage),
            NavigationRouteKind.About => typeof(AboutPage),
            _ => throw new ArgumentOutOfRangeException(nameof(routeKind), routeKind, null)
        };

        private sealed record DetailPageEntry(
            string EntryId,
            DetailRouteKind RouteKind,
            Page Page,
            string BreadcrumbLabel);
    }
}
