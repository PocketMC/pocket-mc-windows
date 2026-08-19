using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Navigation;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Application.Services.Setup;
using PocketMC.Infrastructure.Java;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Desktop.Features.Console;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Mods;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Desktop.Features.Players;

namespace PocketMC.Desktop.Infrastructure
{
    public class AppNavigationService : IAppNavigationService
    {
        private readonly ControlledNavigationStack _detailStack = new();
        private readonly List<DetailPageEntry> _detailPages = new();
        private readonly List<DetailPageEntry> _forwardDetailPages = new();
        private readonly Stack<Type> _shellBackStack = new();
        private readonly Stack<Type> _shellForwardStack = new();
        private Type? _currentShellPageType = typeof(DashboardPage);
        private bool _isNavigatingHistory;
        private readonly IShellUIStateService _uiStateService;
        private readonly IInstanceImportService _importService;
        private readonly IInstanceExportService _exportService;
        private IShellHost? _shellHost;

        public bool CanNavigateForward => _forwardDetailPages.Count > 0 || _shellForwardStack.Count > 0;

        public AppNavigationService(
            IShellUIStateService uiStateService,
            IInstanceImportService importService,
            IInstanceExportService exportService)
        {
            _uiStateService = uiStateService;
            _importService = importService;
            _exportService = exportService;
        }

        public void Initialize(IShellHost shellHost)
        {
            _shellHost = shellHost;
        }

        private bool ConfirmAndCancelActiveOperations()
        {
            if (!_importService.IsActive && !_exportService.IsActive)
            {
                return true;
            }

            var result = AppDialog.ShowResult(
                "Operation In Progress",
                "An import/export operation is currently running. Cancelling now may leave the instance incomplete and all current progress will be lost. Are you sure you want to cancel?",
                AppDialogType.Warning,
                AppDialogButtons.YesNo,
                primaryButtonText: "Continue Operation",
                secondaryButtonText: "Cancel Operation"
            );

            if (result == PocketMC.Desktop.Core.Interfaces.DialogResult.No) // Cancel Operation
            {
                if (_importService.IsActive) _importService.Cancel();
                if (_exportService.IsActive) _exportService.Cancel();

                // Wait until active state is false
                while (_importService.IsActive || _exportService.IsActive)
                {
                    System.Threading.Thread.Sleep(50);
                }
                return true;
            }

            // Continue Operation (cancel navigation)
            return false;
        }

        public bool NavigateToDashboard()
        {
            return NavigateToShellPage(typeof(DashboardPage));
        }

        public bool NavigateToTunnel()
        {
            return NavigateToShellPage(typeof(TunnelPage));
        }

        public bool NavigateToShellPage(Type pageType)
        {
            if (!ConfirmAndCancelActiveOperations()) return false;
            if (_shellHost == null) return false;

            if (!_isNavigatingHistory && _currentShellPageType != null && _currentShellPageType != pageType)
            {
                _shellBackStack.Push(_currentShellPageType);
                _shellForwardStack.Clear();
            }

            bool navigated = _shellHost.ShowShellPage(pageType);
            if (navigated)
            {
                _currentShellPageType = pageType;
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
            if (!ConfirmAndCancelActiveOperations()) return false;
            if (_shellHost == null) return false;

            ValidateDetailTransition(routeKind, backNavigation);

            bool navigated = _shellHost.ShowDetailPage(page, breadcrumbLabel);
            if (!navigated) return false;

            if (clearDetailStack) ClearDetailStack();
            ClearForwardDetailPages();

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
            if (!ConfirmAndCancelActiveOperations()) return false;
            if (_shellHost == null) return false;

            if (_detailPages.Count > 0)
            {
                ControlledBackNavigationResult result = _detailStack.NavigateBack();
                if (!result.Success) return false;

                DetailPageEntry? removedEntry = RemoveDetailEntry(result.RemovedEntryId, dispose: false);
                if (removedEntry != null)
                {
                    _forwardDetailPages.Add(removedEntry);
                }

                if (result.TargetsShellRoute)
                {
                    var pageType = MapShellPageType(result.TargetRoute);
                    _currentShellPageType = pageType;
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

            // Shell back navigation
            if (_shellBackStack.Count > 0)
            {
                Type previousShell = _shellBackStack.Pop();
                if (_currentShellPageType != null)
                {
                    _shellForwardStack.Push(_currentShellPageType);
                }
                _currentShellPageType = previousShell;

                _isNavigatingHistory = true;
                try
                {
                    bool navigated = _shellHost.ShowShellPage(previousShell);
                    if (navigated)
                    {
                        _uiStateService.UpdateBreadcrumb(GetBreadcrumbForPageType(previousShell));
                    }
                    return navigated;
                }
                finally
                {
                    _isNavigatingHistory = false;
                }
            }

            return false;
        }

        public bool NavigateForward()
        {
            if (!ConfirmAndCancelActiveOperations()) return false;
            if (_shellHost == null) return false;

            if (_forwardDetailPages.Count > 0)
            {
                ControlledForwardNavigationResult result = _detailStack.NavigateForward();
                if (!result.Success) return false;

                DetailPageEntry? forwardEntry = _forwardDetailPages.LastOrDefault(entry => entry.EntryId == result.TargetEntryId);
                if (forwardEntry == null) return false;

                _forwardDetailPages.Remove(forwardEntry);
                _detailPages.Add(forwardEntry);

                bool detailNavigated = _shellHost.ShowDetailPage(forwardEntry.Page, forwardEntry.BreadcrumbLabel);
                if (detailNavigated) _uiStateService.UpdateBreadcrumb(forwardEntry.BreadcrumbLabel);
                return detailNavigated;
            }

            // Shell forward navigation
            if (_shellForwardStack.Count > 0)
            {
                Type nextShell = _shellForwardStack.Pop();
                if (_currentShellPageType != null)
                {
                    _shellBackStack.Push(_currentShellPageType);
                }
                _currentShellPageType = nextShell;

                _isNavigatingHistory = true;
                try
                {
                    bool navigated = _shellHost.ShowShellPage(nextShell);
                    if (navigated)
                    {
                        _uiStateService.UpdateBreadcrumb(GetBreadcrumbForPageType(nextShell));
                    }
                    return navigated;
                }
                finally
                {
                    _isNavigatingHistory = false;
                }
            }

            return false;
        }

        private string? GetBreadcrumbForPageType(Type pageType)
        {
            return pageType.Name switch
            {
                nameof(DashboardPage) => "Dashboard",
                nameof(TunnelPage) => "Tunnel",
                nameof(JavaSetupPage) => "Runtimes",
                nameof(AboutPage) => "About",
                nameof(AppSettingsPage) => "Settings",
                nameof(NewInstancePage) => "New Instance",
                nameof(ServerSettingsPage) => "Server Settings",
                nameof(ServerConsolePage) => "Console",
                nameof(PlayerManagementPage) => "Players",
                "PortsMapPage" => "Ports Map",
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
                DetailRouteKind.PluginBrowser => current.RouteKind == DetailRouteKind.ServerSettings || current.RouteKind == DetailRouteKind.NewInstance,
                DetailRouteKind.ImageCrop => current.RouteKind == DetailRouteKind.ServerSettings,
                _ => true
            };

            if (!validParent)
            {
                throw new InvalidOperationException(
                    $"{routeKind} cannot be opened from {current.RouteKind}. The current flow is not allowed.");
            }
        }

        private void ClearForwardDetailPages()
        {
            foreach (DetailPageEntry entry in _forwardDetailPages)
            {
                DisposePage(entry.Page);
            }
            _forwardDetailPages.Clear();
        }

        private void ClearDetailStack()
        {
            foreach (DetailPageEntry entry in _detailPages)
            {
                DisposePage(entry.Page);
            }

            _detailPages.Clear();
            _detailStack.Clear();
            ClearForwardDetailPages();
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
            DetailRouteKind.PlayerManagement => NavigationRouteKind.PlayerManagement,
            DetailRouteKind.ImageCrop => NavigationRouteKind.ImageCrop,
            DetailRouteKind.PortsMap => NavigationRouteKind.PortsMap,
            DetailRouteKind.InstanceImport => NavigationRouteKind.InstanceImport,
            DetailRouteKind.InstanceExport => NavigationRouteKind.InstanceExport,
            DetailRouteKind.PlayitNetworkStatus => NavigationRouteKind.PlayitNetworkStatus,
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
