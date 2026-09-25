using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Infrastructure.Configuration;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Application.Services.Setup;
using PocketMC.Infrastructure.Java;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Desktop.Features.RemoteControl.UI;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.OS;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Shell;

public partial class MainWindow : FluentWindow, IShellHost, IStartupShellHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IShellUIStateService _uiStateService;
    private readonly IShellVisualService _visualService;
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
        ShellStartupCoordinator startupCoordinator,
        ShellViewModel viewModel,
        ILogger<MainWindow> logger)
    {
        _serviceProvider = serviceProvider;
        _uiStateService = uiStateService;
        _visualService = visualService;
        _startupCoordinator = startupCoordinator;
        _viewModel = viewModel;
        _logger = logger;

        DataContext = _viewModel;

        InitializeComponent();
        Title = PocketMC.Infrastructure.Configuration.AppConfig.AppName;
        AppTitleBar.Title = PocketMC.Infrastructure.Configuration.AppConfig.AppName;
        ApplyDynamicWindowSize();

        if (visualService is ShellVisualService concreteVisual)
            concreteVisual.Attach(this);

        RootNavigation.SetServiceProvider(_serviceProvider);
        RootNavigation.Navigating += OnNavigating;
        RootNavigation.Navigated += OnNavigated;

        Closing += MainWindow_Closing;
        _startupCoordinator.AttachHost(this);

        AppTrayIcon.DataContext = _serviceProvider.GetRequiredService<TrayIconViewModel>();

        SourceInitialized += (s, e) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(HwndHook);

            var settingsManager = _serviceProvider.GetService<SettingsManager>();
            var appState = _serviceProvider.GetService<ApplicationState>();
            var settings = appState?.Settings ?? settingsManager?.Load();
            if (settings?.IsWindowMaximized == true)
            {
                WindowState = WindowState.Maximized;
            }
        };
    }


    private void ApplyDynamicWindowSize()
    {
        var settingsManager = _serviceProvider.GetService<SettingsManager>();
        var appState = _serviceProvider.GetService<ApplicationState>();
        var settings = appState?.Settings;

        if (settings == null || (!settings.WindowWidth.HasValue && !settings.IsWindowMaximized))
        {
            try
            {
                var loaded = settingsManager?.Load();
                if (loaded != null)
                {
                    settings = loaded;
                }
            }
            catch { }
        }

        double screenWidth = SystemParameters.WorkArea.Width;
        double screenHeight = SystemParameters.WorkArea.Height;

        if (settings != null && settings.WindowWidth.HasValue && settings.WindowHeight.HasValue &&
            settings.WindowWidth.Value >= 1024 && settings.WindowHeight.Value >= 680)
        {
            Width = Math.Max(1024, Math.Min(settings.WindowWidth.Value, screenWidth));
            Height = Math.Max(680, Math.Min(settings.WindowHeight.Value, screenHeight));
        }
        else
        {
            double targetWidth = Math.Max(1024, Math.Min(1440, screenWidth * 0.85));
            double targetHeight = Math.Max(680, Math.Min(860, screenHeight * 0.85));

            Width = targetWidth;
            Height = targetHeight;
        }

        if (settings?.IsWindowMaximized == true)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        try
        {
            var appState = _serviceProvider.GetService<ApplicationState>();
            var settingsManager = _serviceProvider.GetService<SettingsManager>();
            var settings = appState?.Settings ?? settingsManager?.Load();
            if (settings == null || settingsManager == null) return;

            bool isMaximized = WindowState == WindowState.Maximized;
            settings.IsWindowMaximized = isMaximized;

            if (isMaximized)
            {
                if (RestoreBounds.Width >= 1024 && RestoreBounds.Height >= 680)
                {
                    settings.WindowWidth = RestoreBounds.Width;
                    settings.WindowHeight = RestoreBounds.Height;
                }
            }
            else if (WindowState == WindowState.Normal)
            {
                if (Width >= 1024 && Height >= 680)
                {
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                }
            }

            settingsManager.Save(settings);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist window dimensions/state.");
        }
    }

    private void UpdateDpiScalingIsolation()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.DpiScaleX > 0 && dpi.DpiScaleY > 0)
            {
                double invScaleX = 1.0 / dpi.DpiScaleX;
                double invScaleY = 1.0 / dpi.DpiScaleY;

                if (Math.Abs(invScaleX - 1.0) > 0.001 || Math.Abs(invScaleY - 1.0) > 0.001)
                {
                    RootLayoutGrid.LayoutTransform = new ScaleTransform(invScaleX, invScaleY);
                }
                else
                {
                    RootLayoutGrid.LayoutTransform = Transform.Identity;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to apply DPI scaling isolation.");
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateDpiScalingIsolation();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        HardwareRenderingOptimizer.OptimizeWindow(this);
        UpdateDpiScalingIsolation();
        _visualService.RequestMicaUpdate();

        _startupCoordinator.Start();

        // Pre-warm shell pages during application idle to eliminate navigation lag
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                GetOrCreateShellPage(typeof(AppSettingsPage));
                GetOrCreateShellPage(typeof(TunnelPage));
                GetOrCreateShellPage(typeof(JavaSetupPage));
                GetOrCreateShellPage(typeof(AboutPage));
                GetOrCreateShellPage(typeof(RemoteControlPage));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pre-warming shell page cache completed with note.");
            }
        }));
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
    public void ApplyTheme() => _visualService.ApplyTheme();
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

    public bool ShowPlayitSetupDialog()
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(ShowPlayitSetupDialog);

        var dialog = ActivatorUtilities.CreateInstance<PlayitSetupWizardDialog>(_serviceProvider);
        dialog.Owner = this;
        dialog.ShowDialog();
        return true;
    }



    public void ShowError(string title, string message) =>
        Infrastructure.AppDialog.ShowError(title, message);

    public void ShowWhatsNewDialog(PocketMC.Infrastructure.WhatsNew.ChangelogEntry? changelog, string version)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowWhatsNewDialog(changelog, version));
            return;
        }

        var window = new Features.WhatsNew.WhatsNewWindow(changelog, version);
        try
        {
            if (IsLoaded && IsVisible)
            {
                window.Owner = this;
            }
        }
        catch
        {
            // Owner assignment can fail during startup — continue without it.
        }

        window.ShowDialog();
    }

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
        System.Windows.Application.Current.Shutdown();
    }

    private void HideToTray()
    {
        SaveWindowState();
        Hide();
        _serviceProvider.GetRequiredService<TrayIconViewModel>().EnsureVisible();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowState();

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
        bool appShutdownStarted = System.Windows.Application.Current?.Dispatcher.HasShutdownStarted == true;
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

        e.Cancel = true;
        HideToTray();
        _serviceProvider.GetRequiredService<TrayIconViewModel>().TooltipText = "PocketMC is shutting down...";

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var lifecycle = _serviceProvider.GetRequiredService<IApplicationLifecycleService>();
                await lifecycle.GracefulShutdownAsync();
            }
            catch { }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    RootNavigation.Navigating -= OnNavigating;
                    RootNavigation.Navigated -= OnNavigated;
                    DetachTitleBarContextSource();
                    _startupCoordinator.Shutdown();
                    System.Windows.Application.Current?.Shutdown();
                });
            }
        });
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

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        if (e.Handled) return;
        if (_viewModel.IsNavigationLocked) return;

        if (e.ChangedButton == MouseButton.XButton1)
        {
            if (TryNavigateBack())
            {
                e.Handled = true;
            }
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            if (TryNavigateForward())
            {
                e.Handled = true;
            }
        }
    }

    private bool TryNavigateBack()
    {
        if (_currentPage is ISupportsKeyboardBackNavigation support)
        {
            if (support.HandleBackNavigation())
            {
                return true;
            }
        }

        var nav = _serviceProvider.GetService<IAppNavigationService>();
        return nav != null && nav.NavigateBack();
    }

    private bool TryNavigateForward()
    {
        var nav = _serviceProvider.GetService<IAppNavigationService>();
        return nav != null && nav.NavigateForward();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled) return;

        // If navigation is locked (e.g. initial setup), ignore hotkeys
        if (_viewModel.IsNavigationLocked) return;

        // Escape to go back
        if (e.Key == Key.Escape)
        {
            if (_currentPage is ISupportsKeyboardBackNavigation support)
            {
                if (support.HandleBackNavigation())
                {
                    e.Handled = true;
                    return;
                }
            }

            var focused = FocusManager.GetFocusedElement(this);
            if (focused is not System.Windows.Controls.Primitives.TextBoxBase &&
                focused is not System.Windows.Controls.PasswordBox)
            {
                var nav = _serviceProvider.GetService<IAppNavigationService>();
                if (nav != null && nav.NavigateBack())
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // PageUp or BrowserBack to navigate back
        if (e.Key == Key.PageUp || e.Key == Key.BrowserBack)
        {
            if (TryNavigateBack())
            {
                e.Handled = true;
                return;
            }
        }

        // PageDown or BrowserForward to navigate forward
        if (e.Key == Key.PageDown || e.Key == Key.BrowserForward)
        {
            if (TryNavigateForward())
            {
                e.Handled = true;
                return;
            }
        }

        // F5 or Ctrl + R to refresh dashboard instances
        if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
        {
            var dashboardVm = _serviceProvider.GetService<DashboardViewModel>();
            if (dashboardVm != null)
            {
                dashboardVm.RefreshInstancesCommand.Execute(null);
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
                        handled = nav.NavigateToShellPage(typeof(JavaSetupPage));
                        break;
                    case Key.D5:
                    case Key.NumPad5:
                        handled = nav.NavigateToShellPage(typeof(AppSettingsPage));
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

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _explicitExitRequested = true;
        _serviceProvider.GetRequiredService<TrayIconViewModel>().TooltipText = "PocketMC is shutting down...";

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var lifecycle = _serviceProvider.GetRequiredService<IApplicationLifecycleService>();
                await lifecycle.GracefulShutdownAsync();
            }
            catch { }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current?.Shutdown();
                });
            }
        });
    }

    // ── Auto-hide taskbar & Multi-monitor Maximize Support ───────────────────────

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint ABM_GETSTATE = 0x00000004;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const int ABS_AUTOHIDE = 0x0000001;

    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    RECT rcWork = mi.rcWork;
                    RECT rcMonitor = mi.rcMonitor;

                    // Check if auto-hide taskbar is active on the system
                    var abd = new APPBARDATA();
                    abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                    uint state = (uint)SHAppBarMessage(ABM_GETSTATE, ref abd);
                    bool isAutoHide = (state & ABS_AUTOHIDE) != 0;

                    if (isAutoHide)
                    {
                        // When taskbar is auto-hidden, rcWork == rcMonitor.
                        // We must leave a 2-pixel margin on the taskbar edge so Windows can detect hover to show the taskbar.
                        SHAppBarMessage(ABM_GETTASKBARPOS, ref abd);
                        if (abd.rc.left >= rcMonitor.left && abd.rc.right <= rcMonitor.right &&
                            abd.rc.top >= rcMonitor.top && abd.rc.bottom <= rcMonitor.bottom)
                        {
                            switch (abd.uEdge)
                            {
                                case ABE_LEFT:
                                    rcWork.left += 2;
                                    break;
                                case ABE_TOP:
                                    rcWork.top += 2;
                                    break;
                                case ABE_RIGHT:
                                    rcWork.right -= 2;
                                    break;
                                case ABE_BOTTOM:
                                    rcWork.bottom -= 2;
                                    break;
                            }
                        }
                        else
                        {
                            rcWork.bottom -= 2;
                        }
                    }

                    mmi.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left);
                    mmi.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top);
                    mmi.ptMaxSize.x = Math.Abs(rcWork.right - rcWork.left);
                    mmi.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top);
                    mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
                    mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }
        catch
        {
            // Non-critical window sizing fallback
        }
    }
}

