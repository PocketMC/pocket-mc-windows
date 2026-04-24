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
using PocketMC.Desktop.Infrastructure;
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
        ApplyDynamicWindowSize();

        if (visualService is ShellVisualService concreteVisual)
            concreteVisual.Attach(this, MicaFallbackBackground);

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
        _startupCoordinator.Start();
        _viewModel.InitializeUpdateCheck();
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
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
        pageType == typeof(AppSettingsPage);

    public bool ShowShellPage(Type pageType, object? parameter = null)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => ShowShellPage(pageType, parameter));

        Page shellPage = GetOrCreateShellPage(pageType);
        bool replaced = RootNavigation.ReplaceContent(shellPage, parameter);
        if (replaced)
        {
            _lastShellPageType = pageType;
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
            AttachTitleBarContextSource(page as ITitleBarContextSource);
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
    }

    private NavigationViewItem? GetShellNavigationItem(Type? pageType)
    {
        if (pageType == typeof(DashboardPage)) return NavDashboard;
        if (pageType == typeof(TunnelPage)) return NavTunnel;
        if (pageType == typeof(JavaSetupPage)) return NavJavaSetup;
        if (pageType == typeof(AboutPage)) return NavAbout;
        if (pageType == typeof(AppSettingsPage)) return NavSettings;
        return null;
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
                NavAbout.IsEnabled = NavSettings.IsEnabled = false;
            _uiStateService.UpdateBreadcrumb(null);
        }
        else
        {
            _viewModel.IsPaneVisible = true;
            _viewModel.IsPaneToggleVisible = true;
            NavDashboard.IsEnabled = NavTunnel.IsEnabled = NavJavaSetup.IsEnabled =
                NavAbout.IsEnabled = NavSettings.IsEnabled = true;
        }
    }

    public void RequestMicaUpdate() => _visualService.RequestMicaUpdate();
    public void RequestThemeUpdate(string theme) => _visualService.ApplyTheme(theme);

    public void ShowRootDirectorySetup()
    {
        SetNavigationLocked(true);
        var setupPage = ActivatorUtilities.CreateInstance<RootDirectorySetupPage>(_serviceProvider);
        setupPage.DirectorySelected += (s, path) => _startupCoordinator.CompleteRootDirectorySelection(path);
        RootNavigation.ReplaceContent(setupPage, null);
    }

    public void CompleteRootDirectorySetup() => SetNavigationLocked(false);

    public bool NavigateToDashboard() =>
        _serviceProvider.GetRequiredService<IAppNavigationService>().NavigateToDashboard();

    public bool NavigateToTunnel() =>
        _serviceProvider.GetRequiredService<IAppNavigationService>().NavigateToTunnel();



    public void ShowError(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

    public void ShutdownApplication() => Application.Current.Shutdown();
    public void CloseApp() => Application.Current.Shutdown();

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var processManager = _serviceProvider.GetRequiredService<ServerProcessManager>();
        if (processManager.ActiveProcesses.Count > 0)
        {
            e.Cancel = true;
            Hide();
            _serviceProvider.GetRequiredService<TrayIconViewModel>().EnsureVisible();
            return;
        }

        RootNavigation.Navigating -= OnNavigating;
        RootNavigation.Navigated -= OnNavigated;
        DetachTitleBarContextSource();
        _startupCoordinator.Shutdown();
    }

    private void AppTrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) =>
        TrayOpen_Click(sender, e);

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
    {
        _serviceProvider.GetRequiredService<TrayIconViewModel>().Hide();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        var lifecycle = _serviceProvider.GetRequiredService<IApplicationLifecycleService>();
        await lifecycle.GracefulShutdownAsync();
        Application.Current.Shutdown();
    }
}
