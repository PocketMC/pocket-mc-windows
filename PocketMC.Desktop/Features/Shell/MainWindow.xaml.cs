using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Desktop.Features.RemoteControl.UI;
using PocketMC.Desktop.Infrastructure;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Shell;

public partial class MainWindow : FluentWindow, IShellHost, IStartupShellHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IShellUIStateService _uiStateService;
    private readonly IShellVisualService _visualService;
    private readonly WindowsCornerService _windowsCornerService;
    private readonly ShellStartupCoordinator _startupCoordinator;
    private readonly ShellViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;

    private Type _lastShellPageType = typeof(DashboardPage);
    private ITitleBarContextSource? _titleBarContextSource;
    private readonly Dictionary<Type, Page> _shellPageCache = new();
    private bool _explicitExitRequested;
    private Page? _currentPage;
    private static readonly HashSet<Type> ShellOwnedScrollPageTypes = new()
    {
        typeof(DashboardPage),
        typeof(TunnelPage),
        typeof(PortsMapPage),
        typeof(RemoteControlPage),
        typeof(JavaSetupPage),
        typeof(AboutPage),
        typeof(AppSettingsPage)
    };

    public MainWindow(
        IServiceProvider serviceProvider,
        IShellUIStateService uiStateService,
        IShellVisualService visualService,
        WindowsCornerService windowsCornerService,
        ShellStartupCoordinator startupCoordinator,
        ShellViewModel viewModel,
        ILogger<MainWindow> logger)
    {
        _serviceProvider = serviceProvider;
        _uiStateService = uiStateService;
        _visualService = visualService;
        _windowsCornerService = windowsCornerService;
        _startupCoordinator = startupCoordinator;
        _viewModel = viewModel;
        _logger = logger;

        DataContext = _viewModel;

        InitializeComponent();
        _windowsCornerService.ApplyWindows10RoundedCorners(this);
        ApplyDynamicWindowSize();

        if (visualService is ShellVisualService concreteVisual)
            concreteVisual.Attach(this);

        RootNavigation.SetServiceProvider(_serviceProvider);
        RootNavigation.Navigating += OnNavigating;
        RootNavigation.Navigated += OnNavigated;

        Closing += MainWindow_Closing;
        _startupCoordinator.AttachHost(this);

        AppTrayIcon.DataContext = _serviceProvider.GetRequiredService<TrayIconViewModel>();
    }


    private void ApplyDynamicWindowSize()
    {
        const double widthRatio = 0.75;
        const double heightRatio = 0.85;
        const double minWidth = 960;
        const double minHeight = 640;

        Width = Math.Max(minWidth, SystemParameters.WorkArea.Width * widthRatio);
        Height = Math.Max(minHeight, SystemParameters.WorkArea.Height * heightRatio);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _visualService.ApplyTheme();
        _visualService.SetWindowActive(IsActive);
        _visualService.RequestMicaUpdate();
        
        _startupCoordinator.Start();
        _viewModel.InitializeUpdateCheck();
    }

    private void Window_Activated(object? sender, EventArgs e) =>
        _visualService.SetWindowActive(true);

    private void Window_Deactivated(object? sender, EventArgs e) =>
        _visualService.SetWindowActive(false);

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page is Page navigatedPage)
        {
            _currentPage = navigatedPage;
            DisableShellOwnedScrollHost(navigatedPage);
        }

        var pageType = args.Page?.GetType();
        if (IsShellPageType(pageType))
        {
            _lastShellPageType = pageType!;
            DetachTitleBarContextSource();
            SyncNavigationSelection(pageType);
        }
    }

    private static bool IsShellPageType(Type? pageType) =>
        pageType == typeof(DashboardPage) ||
        pageType == typeof(TunnelPage) ||
        pageType == typeof(JavaSetupPage) ||
        pageType == typeof(AboutPage) ||
        pageType == typeof(AppSettingsPage) ||
        pageType == typeof(RemoteControlPage);

    public bool ShowShellPage(Type pageType, object? parameter = null)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => ShowShellPage(pageType, parameter));

        Page shellPage = GetOrCreateShellPage(pageType);
        bool replaced = RootNavigation.ReplaceContent(shellPage, parameter);
        if (replaced)
        {
            _currentPage = shellPage;
            _lastShellPageType = pageType;
            DisableShellOwnedScrollHost(shellPage);
            DetachTitleBarContextSource();
            SyncNavigationSelection(pageType);
        }
        return replaced;
    }

    public bool ShowDetailPage(Page page, string breadcrumbLabel)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => ShowDetailPage(page, breadcrumbLabel));

        bool replaced = RootNavigation.ReplaceContent(page, null);
        if (replaced)
        {
            _currentPage = page;
            DisableShellOwnedScrollHost(page);
            AttachTitleBarContextSource(page as ITitleBarContextSource);
        }
        return replaced;
    }

    public bool NavigateBackFromDetail(Type defaultShellPage)
    {
        if (_viewModel.IsNavigationLocked) return false;
        return ShowShellPage(_lastShellPageType ?? defaultShellPage);
    }

    private void OnNavigating(NavigationView sender, NavigatingCancelEventArgs args)
    {
        Type? pageType = args.Page?.GetType();
        if (pageType == null && args.Page is Type t) pageType = t;

        if (_viewModel.IsNavigationLocked)
        {
            if (pageType == typeof(RootDirectorySetupPage)) return;
            args.Cancel = true;
            return;
        }

        if (PocketMC.Desktop.Features.InstanceCreation.NewInstancePage.InstanceCreatePageIsOpen && 
            PocketMC.Desktop.Features.InstanceCreation.NewInstancePage.IsDownloadInProgress)
        {
            args.Cancel = true;
            return;
        }

        var importService = _serviceProvider.GetRequiredService<IInstanceImportService>();
        var exportService = _serviceProvider.GetRequiredService<IInstanceExportService>();
        if (importService.IsActive || exportService.IsActive)
        {
            var dialogResult = Infrastructure.AppDialog.ShowResult(
                "Operation In Progress",
                "An import/export operation is currently running. Cancelling now may leave the instance incomplete and all current progress will be lost. Are you sure you want to cancel?",
                Infrastructure.AppDialogType.Warning,
                Infrastructure.AppDialogButtons.YesNo,
                primaryButtonText: "Continue Operation",
                secondaryButtonText: "Cancel Operation"
            );

            if (dialogResult == PocketMC.Desktop.Core.Interfaces.DialogResult.No) // Cancel Operation
            {
                if (importService.IsActive) importService.Cancel();
                if (exportService.IsActive) exportService.Cancel();

                while (importService.IsActive || exportService.IsActive)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            else // Continue Operation
            {
                args.Cancel = true;
                return;
            }
        }

        if (!IsShellPageType(pageType)) return;

        args.Cancel = true;
        if (_serviceProvider.GetService<IAppNavigationService>() is { } nav)
            nav.NavigateToShellPage(pageType!);
    }

    private void SyncNavigationSelection(Type? pageType)
    {
        if (!IsShellPageType(pageType)) return;
        NavigationViewItem? targetItem = GetShellNavigationItem(pageType);
        if (targetItem == null) return;

        try
        {
            typeof(NavigationView).GetProperty("SelectedItem")?.SetValue(RootNavigation, targetItem);
        }
        catch { }

        SetNavigationItemActiveState(NavDashboard, ReferenceEquals(targetItem, NavDashboard));
        SetNavigationItemActiveState(NavTunnel, ReferenceEquals(targetItem, NavTunnel));
        SetNavigationItemActiveState(NavJavaSetup, ReferenceEquals(targetItem, NavJavaSetup));
        SetNavigationItemActiveState(NavAbout, ReferenceEquals(targetItem, NavAbout));
        SetNavigationItemActiveState(NavSettings, ReferenceEquals(targetItem, NavSettings));
        SetNavigationItemActiveState(NavRemoteControl, ReferenceEquals(targetItem, NavRemoteControl));
    }

    private NavigationViewItem? GetShellNavigationItem(Type? pageType)
    {
        if (pageType == typeof(DashboardPage)) return NavDashboard;
        if (pageType == typeof(TunnelPage)) return NavTunnel;
        if (pageType == typeof(JavaSetupPage)) return NavJavaSetup;
        if (pageType == typeof(AboutPage)) return NavAbout;
        if (pageType == typeof(AppSettingsPage)) return NavSettings;
        if (pageType == typeof(RemoteControlPage)) return NavRemoteControl;
        return null;
    }

    private void DisableShellOwnedScrollHost(Page page)
    {
        if (!ShellOwnedScrollPageTypes.Contains(page.GetType()))
            return;

        Dispatcher.BeginInvoke(
            new Action(() => ScrollViewerHelper.DisableAncestorScrollViewers(page)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private Page GetOrCreateShellPage(Type pageType)
    {
        if (_shellPageCache.TryGetValue(pageType, out Page? cached)) return cached;
        Page shellPage = (Page)_serviceProvider.GetRequiredService(pageType);
        _shellPageCache[pageType] = shellPage;
        return shellPage;
    }

    private void SetNavigationItemActiveState(NavigationViewItem item, bool isActive)
    {
        try { item.GetType().GetProperty("IsActive")?.SetValue(item, isActive); } catch { }
    }

    private void AttachTitleBarContextSource(ITitleBarContextSource? source)
    {
        DetachTitleBarContextSource();
        _titleBarContextSource = source;
        if (_titleBarContextSource != null)
            _titleBarContextSource.TitleBarContextChanged += OnTitleBarContextChanged;
        UpdateTitleBarContext();
    }

    private void DetachTitleBarContextSource()
    {
        if (_titleBarContextSource != null)
        {
            _titleBarContextSource.TitleBarContextChanged -= OnTitleBarContextChanged;
            _titleBarContextSource = null;
        }
        _uiStateService.ClearTitleBarContext();
    }

    private void OnTitleBarContextChanged() => Dispatcher.Invoke(UpdateTitleBarContext);

    private void UpdateTitleBarContext()
    {
        if (_titleBarContextSource == null) return;
        _uiStateService.SetTitleBarContext(
            _titleBarContextSource.TitleBarContextTitle,
            _titleBarContextSource.TitleBarContextStatusText,
            _titleBarContextSource.TitleBarContextStatusBrush);
    }

    public void SetNavigationLocked(bool isLocked)
    {
        _viewModel.IsNavigationLocked = isLocked;
        if (isLocked)
        {
            DetachTitleBarContextSource();
            _viewModel.IsPaneVisible = false;
            _viewModel.IsPaneToggleVisible = false;
            NavDashboard.IsEnabled = NavTunnel.IsEnabled = NavJavaSetup.IsEnabled =
                NavAbout.IsEnabled = NavSettings.IsEnabled = NavRemoteControl.IsEnabled = false;
            _uiStateService.UpdateBreadcrumb(null);
        }
        else
        {
            _viewModel.IsPaneVisible = true;
            _viewModel.IsPaneToggleVisible = true;
            NavDashboard.IsEnabled = NavTunnel.IsEnabled = NavJavaSetup.IsEnabled =
                NavAbout.IsEnabled = NavSettings.IsEnabled = NavRemoteControl.IsEnabled = true;
        }
    }

    public void RequestMicaUpdate() => _visualService.RequestMicaUpdate();

    public void ShowRootDirectorySetup()
    {
        SetNavigationLocked(true);
        var setupPage = ActivatorUtilities.CreateInstance<RootDirectorySetupPage>(_serviceProvider);
        setupPage.DirectorySelected += (s, path) => _startupCoordinator.CompleteRootDirectorySelection(path);
        bool replaced = RootNavigation.ReplaceContent(setupPage, null);
        if (replaced) _currentPage = setupPage;
    }

    public void CompleteRootDirectorySetup() => SetNavigationLocked(false);

    public bool NavigateToDashboard() =>
        _serviceProvider.GetRequiredService<IAppNavigationService>().NavigateToDashboard();

    public bool NavigateToTunnel() =>
        _serviceProvider.GetRequiredService<IAppNavigationService>().NavigateToTunnel();

    public bool NavigateToPlayitSetup()
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(NavigateToPlayitSetup);

        var nav = _serviceProvider.GetRequiredService<IAppNavigationService>();
        var wizardPage = ActivatorUtilities.CreateInstance<PlayitSetupWizardPage>(_serviceProvider);
        return nav.NavigateToDetailPage(
            wizardPage,
            "Playit Agent Setup",
            DetailRouteKind.PlayitSetupWizard,
            DetailBackNavigation.Dashboard,
            clearDetailStack: true);
    }



    public void ShowError(string title, string message) =>
        Infrastructure.AppDialog.ShowError(title, message);

    public void ShowMinimizedToTray()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        Show();
        HideToTray();
    }

    public void ShutdownApplication() => RequestApplicationShutdown();
    public void CloseApp() => RequestApplicationShutdown();

    private void RequestApplicationShutdown()
    {
        _explicitExitRequested = true;
        Application.Current.Shutdown();
    }

    private void HideToTray()
    {
        Hide();
        _serviceProvider.GetRequiredService<TrayIconViewModel>().EnsureVisible();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var importService = _serviceProvider.GetRequiredService<IInstanceImportService>();
        var exportService = _serviceProvider.GetRequiredService<IInstanceExportService>();
        if (importService.IsActive || exportService.IsActive)
        {
            e.Cancel = true;

            var dialogResult = Infrastructure.AppDialog.ShowResult(
                "Operation In Progress",
                "An import/export operation is currently running. Cancelling now may leave the instance incomplete and all current progress will be lost. Are you sure you want to cancel?",
                Infrastructure.AppDialogType.Warning,
                Infrastructure.AppDialogButtons.YesNo,
                primaryButtonText: "Continue Operation",
                secondaryButtonText: "Cancel Operation"
            );

            if (dialogResult == PocketMC.Desktop.Core.Interfaces.DialogResult.No) // Cancel Operation
            {
                if (importService.IsActive) importService.Cancel();
                if (exportService.IsActive) exportService.Cancel();

                var controller = new System.Windows.Threading.DispatcherFrame();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (importService.IsActive || exportService.IsActive)
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                    }
                    controller.Continue = false;
                });

                var originalCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    System.Windows.Threading.Dispatcher.PushFrame(controller);
                }
                finally
                {
                    Mouse.OverrideCursor = originalCursor;
                }

                CloseApp();
            }
            return;
        }

        bool downloadExitConfirmed = false;
        if (PocketMC.Desktop.Features.InstanceCreation.NewInstancePage.InstanceCreatePageIsOpen && 
            PocketMC.Desktop.Features.InstanceCreation.NewInstancePage.IsDownloadInProgress)
        {
            var result = Infrastructure.AppDialog.Confirm(
                "Cancel Download?",
                "A download is in progress. Are you sure you want to exit? The download will be cancelled.");
                
            if (!result)
            {
                e.Cancel = true;
                return;
            }

            downloadExitConfirmed = true;
        }

        var processManager = _serviceProvider.GetRequiredService<ServerProcessManager>();
        bool hasRunningServers = processManager.ActiveProcesses.Count > 0;
        bool appShutdownStarted = Application.Current?.Dispatcher.HasShutdownStarted == true;
        bool explicitExitRequested = _explicitExitRequested ||
                                     appShutdownStarted ||
                                     (downloadExitConfirmed && !hasRunningServers);
        bool minimizeToTrayOnClose = _serviceProvider
            .GetRequiredService<ApplicationState>()
            .Settings
            .MinimizeToTrayOnClose;
        bool isRemoteControlRunning = _serviceProvider
            .GetRequiredService<ApplicationState>()
            .Settings
            .RemoteControl.Enabled;

        if (MainWindowCloseBehavior.Decide(explicitExitRequested, hasRunningServers, minimizeToTrayOnClose, isRemoteControlRunning)
            == MainWindowCloseAction.HideToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        RootNavigation.Navigating -= OnNavigating;
        RootNavigation.Navigated -= OnNavigated;
        DetachTitleBarContextSource();
        _startupCoordinator.Shutdown();
        Application.Current?.Shutdown();
    }

    private void AppTrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) =>
        TrayOpen_Click(sender, e);

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
    {
        _serviceProvider.GetRequiredService<TrayIconViewModel>().Hide();
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled) return;

        // If navigation is locked (e.g. initial setup), ignore hotkeys
        if (_viewModel.IsNavigationLocked) return;

        // Escape to go back, but only if not typing in a text input
        if (e.Key == Key.Escape)
        {
            var focused = FocusManager.GetFocusedElement(this);
            if (focused is not System.Windows.Controls.Primitives.TextBoxBase && 
                focused is not System.Windows.Controls.PasswordBox)
            {
                if (_currentPage is ISupportsKeyboardBackNavigation support)
                {
                    if (support.HandleBackNavigation())
                    {
                        e.Handled = true;
                        return;
                    }
                }

                var nav = _serviceProvider.GetService<IAppNavigationService>();
                if (nav != null && nav.NavigateBack())
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // Alt + Left or BrowserBack to navigate back
        if ((e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) || e.Key == Key.BrowserBack)
        {
            if (_currentPage is ISupportsKeyboardBackNavigation support)
            {
                if (support.HandleBackNavigation())
                {
                    e.Handled = true;
                    return;
                }
            }

            var nav = _serviceProvider.GetService<IAppNavigationService>();
            if (nav != null && nav.NavigateBack())
            {
                e.Handled = true;
                return;
            }
        }

        // Ctrl + N to open New Instance page
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var nav = _serviceProvider.GetService<IAppNavigationService>();
            if (nav != null)
            {
                var page = ActivatorUtilities.CreateInstance<PocketMC.Desktop.Features.InstanceCreation.NewInstancePage>(_serviceProvider);
                if (nav.NavigateToDetailPage(page, "New Instance", DetailRouteKind.NewInstance, DetailBackNavigation.Dashboard, true))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // Ctrl + 1..6 or Ctrl + , navigation shortcuts
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var nav = _serviceProvider.GetService<IAppNavigationService>();
            if (nav != null)
            {
                bool handled = false;
                switch (e.Key)
                {
                    case Key.D1:
                    case Key.NumPad1:
                        handled = nav.NavigateToDashboard();
                        break;
                    case Key.D2:
                    case Key.NumPad2:
                        handled = nav.NavigateToTunnel();
                        break;
                    case Key.D3:
                    case Key.NumPad3:
                        handled = nav.NavigateToShellPage(typeof(RemoteControlPage));
                        break;
                    case Key.D4:
                    case Key.NumPad4:
                        handled = nav.NavigateToShellPage(typeof(AppSettingsPage));
                        break;
                    case Key.D5:
                    case Key.NumPad5:
                        handled = nav.NavigateToShellPage(typeof(JavaSetupPage));
                        break;
                    case Key.D6:
                    case Key.NumPad6:
                        handled = nav.NavigateToShellPage(typeof(AboutPage));
                        break;
                    case Key.OemComma:
                        handled = nav.NavigateToShellPage(typeof(AppSettingsPage));
                        break;
                }
                if (handled)
                {
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private async void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _explicitExitRequested = true;
        var lifecycle = _serviceProvider.GetRequiredService<IApplicationLifecycleService>();
        await lifecycle.GracefulShutdownAsync();
        Application.Current.Shutdown();
    }
}
