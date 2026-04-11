using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Reflection;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SettingsManager _settingsManager;
    private readonly ApplicationState _applicationState;
    private readonly ResourceMonitorService _globalMonitor;
    private readonly BackupSchedulerService _backupScheduler;
    private readonly ServerProcessManager _serverProcessManager;
    private readonly JavaProvisioningService _javaProvisioningService;
    private readonly PlayitAgentService _playitAgentService;
    private readonly ILogger<MainWindow> _logger;
    private Type _lastShellPageType = typeof(DashboardPage);
    private bool _isShowingDetailPage;
    private ITitleBarContextSource? _titleBarContextSource;
    private Type? _paneManagedDetailPageType;
    private bool _paneWasOpenBeforeManagedDetail;
    private bool _managedDetailPaneModified;
    private bool _isApplyingPaneStateProgrammatically;
    private bool _startupServicesStarted;
    private bool _playitStartupAttempted;
    private bool _isNavigationLockedToRootSetup;

    public MainWindow(
        IServiceProvider serviceProvider,
        SettingsManager settingsManager,
        ApplicationState applicationState,
        ResourceMonitorService globalMonitor,
        BackupSchedulerService backupScheduler,
        ServerProcessManager serverProcessManager,
        JavaProvisioningService javaProvisioningService,
        PlayitAgentService playitAgentService,
        ILogger<MainWindow> logger)
    {
        _serviceProvider = serviceProvider;
        _settingsManager = settingsManager;
        _applicationState = applicationState;
        _globalMonitor = globalMonitor;
        _backupScheduler = backupScheduler;
        _serverProcessManager = serverProcessManager;
        _javaProvisioningService = javaProvisioningService;
        _playitAgentService = playitAgentService;
        _logger = logger;

        InitializeComponent();

        // Wire up WPF-UI theme engine
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);

        // Set up the NavigationView to resolve pages via DI
        RootNavigation.SetServiceProvider(_serviceProvider);

        // Listen for navigation events to update breadcrumb
        RootNavigation.Navigating += OnNavigating;
        RootNavigation.Navigated += OnNavigated;
        RootNavigation.PaneOpened += RootNavigation_PaneOpened;
        RootNavigation.PaneClosed += RootNavigation_PaneClosed;

        Closing += MainWindow_Closing;
        _globalMonitor.OnGlobalMetricsUpdated += UpdateGlobalHealth;
        _playitAgentService.OnClaimUrlReceived += OnPlayitClaimUrlReceived;
        _playitAgentService.OnTunnelRunning += OnPlayitTunnelRunning;

        // Win10 fallback: listen for wallpaper changes to refresh simulated Mica
        if (!WallpaperMicaService.IsWindows11OrLater)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    // ──────────────────────────────────────────────
    //  Navigation & Breadcrumb
    // ──────────────────────────────────────────────

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        var pageType = args.Page?.GetType();
        if (IsShellPageType(pageType))
        {
            _lastShellPageType = pageType!;
            _isShowingDetailPage = false;
            DetachTitleBarContextSource();
            SyncNavigationSelection(pageType);
        }
        UpdateBreadcrumb(pageType);
    }

    private void UpdateBreadcrumb(Type? pageType)
    {
        string? label = pageType?.Name switch
        {
            nameof(DashboardPage)      => "Dashboard",
            nameof(TunnelPage)         => "Tunnel",
            nameof(JavaSetupPage)       => "Java Setup",
            nameof(AboutPage)           => "About",
            nameof(AppSettingsPage)     => "Settings",
            nameof(NewInstancePage)     => "New Instance",
            nameof(ServerSettingsPage)  => "Server Settings",
            nameof(ServerConsolePage)   => "Console",
            _ => null
        };

        UpdateBreadcrumbLabel(label);
    }

    private void UpdateBreadcrumbLabel(string? label)
    {
        if (!string.IsNullOrEmpty(label))
        {
            BreadcrumbSeparator.Visibility = Visibility.Visible;
            BreadcrumbCurrent.Text = label;
            BreadcrumbCurrent.Visibility = Visibility.Visible;
        }
        else
        {
            BreadcrumbSeparator.Visibility = Visibility.Collapsed;
            BreadcrumbCurrent.Visibility = Visibility.Collapsed;
        }
    }

    private static bool IsShellPageType(Type? pageType) =>
        pageType == typeof(DashboardPage) ||
        pageType == typeof(TunnelPage) ||
        pageType == typeof(JavaSetupPage) ||
        pageType == typeof(AboutPage) ||
        pageType == typeof(AppSettingsPage);

    /// <summary>
    /// Allows child pages to navigate within the NavigationView.
    /// Called from pages that need to navigate to ServerSettings/Console.
    /// </summary>
    public void NavigateToPage(Type pageType, object? dataContext = null)
    {
        NavigateToShellPage(pageType, dataContext);
    }

    public bool NavigateToShellPage(Type pageType, object? parameter = null)
    {
        if (!CanNavigateToPage(pageType))
        {
            return false;
        }

        if (_isShowingDetailPage && IsShellPageType(pageType))
        {
            return ReplaceShellContent(pageType);
        }

        bool navigated = RootNavigation.Navigate(pageType, parameter);
        if (navigated && IsShellPageType(pageType))
        {
            _lastShellPageType = pageType;
            _isShowingDetailPage = false;
            RestorePaneAfterManagedDetailIfNeeded();
            SyncNavigationSelection(pageType);
            UpdateBreadcrumb(pageType);
        }

        return navigated;
    }

    public bool NavigateToDashboard()
    {
        if (!CanNavigateToPage(typeof(DashboardPage)))
        {
            return false;
        }

        return ReplaceShellContent(typeof(DashboardPage));
    }

    public bool NavigateToDetailPage(Page page, string breadcrumbLabel)
    {
        if (!CanNavigateToPage(page.GetType()))
        {
            return false;
        }

        bool replaced = RootNavigation.ReplaceContent(page, null);
        if (replaced)
        {
            _isShowingDetailPage = true;
            ApplyDetailPageShellState(page);
            AttachTitleBarContextSource(page as ITitleBarContextSource);
            UpdateBreadcrumbLabel(breadcrumbLabel);
        }

        return replaced;
    }

    public bool NavigateBackFromDetail()
    {
        if (_isNavigationLockedToRootSetup)
        {
            return false;
        }

        return NavigateToDashboard();
    }

    private bool ReplaceShellContent(Type pageType)
    {
        if (!CanNavigateToPage(pageType))
        {
            return false;
        }

        bool replaced = RootNavigation.ReplaceContent(pageType);
        if (replaced)
        {
            _lastShellPageType = pageType;
            _isShowingDetailPage = false;
            RestorePaneAfterManagedDetailIfNeeded();
            DetachTitleBarContextSource();
            SyncNavigationSelection(pageType);
            UpdateBreadcrumb(pageType);
        }

        return replaced;
    }

    private void OnNavigating(NavigationView sender, NavigatingCancelEventArgs args)
    {
        if (!_isNavigationLockedToRootSetup)
        {
            return;
        }

        Type? pageType = GetRequestedPageType(args.Page);
        if (pageType == typeof(RootDirectorySetupPage))
        {
            return;
        }

        args.Cancel = true;
        _logger.LogDebug(
            "Blocked navigation to {PageType} until the PocketMC root directory has been selected.",
            pageType?.Name ?? "<unknown>");
    }

    private void SyncNavigationSelection(Type? pageType)
    {
        if (!IsShellPageType(pageType))
        {
            return;
        }

        NavigationViewItem? targetItem = GetShellNavigationItem(pageType);
        if (targetItem == null)
        {
            return;
        }

        try
        {
            PropertyInfo? selectedItemProperty = RootNavigation.GetType().GetProperty("SelectedItem");
            selectedItemProperty?.SetValue(RootNavigation, targetItem);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to synchronize NavigationView selected item for page {PageType}.", pageType?.Name);
        }

        SetNavigationItemActiveState(NavDashboard, ReferenceEquals(targetItem, NavDashboard));
        SetNavigationItemActiveState(NavTunnel, ReferenceEquals(targetItem, NavTunnel));
        SetNavigationItemActiveState(NavJavaSetup, ReferenceEquals(targetItem, NavJavaSetup));
        SetNavigationItemActiveState(NavAbout, ReferenceEquals(targetItem, NavAbout));
        SetNavigationItemActiveState(NavSettings, ReferenceEquals(targetItem, NavSettings));
    }

    private void ClearNavigationSelection()
    {
        try
        {
            PropertyInfo? selectedItemProperty = RootNavigation.GetType().GetProperty("SelectedItem");
            selectedItemProperty?.SetValue(RootNavigation, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clear the NavigationView selected item.");
        }

        SetNavigationItemActiveState(NavDashboard, false);
        SetNavigationItemActiveState(NavTunnel, false);
        SetNavigationItemActiveState(NavJavaSetup, false);
        SetNavigationItemActiveState(NavAbout, false);
        SetNavigationItemActiveState(NavSettings, false);
    }

    private NavigationViewItem? GetShellNavigationItem(Type? pageType)
    {
        if (pageType == typeof(DashboardPage))
        {
            return NavDashboard;
        }

        if (pageType == typeof(TunnelPage))
        {
            return NavTunnel;
        }

        if (pageType == typeof(JavaSetupPage))
        {
            return NavJavaSetup;
        }

        if (pageType == typeof(AboutPage))
        {
            return NavAbout;
        }

        if (pageType == typeof(AppSettingsPage))
        {
            return NavSettings;
        }

        return null;
    }

    private void SetNavigationItemActiveState(NavigationViewItem item, bool isActive)
    {
        try
        {
            PropertyInfo? isActiveProperty = item.GetType().GetProperty("IsActive");
            if (isActiveProperty?.CanWrite == true)
            {
                isActiveProperty.SetValue(item, isActive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update NavigationViewItem active state for {ItemName}.", item.Name);
        }
    }

    private void ApplyDetailPageShellState(Page page)
    {
        if (ShouldCollapsePaneForDetail(page))
        {
            CollapsePaneForDetail(page.GetType());
            return;
        }

        RestorePaneAfterManagedDetailIfNeeded();
    }

    private static bool ShouldCollapsePaneForDetail(Page page) =>
        page is ServerConsolePage || page is ServerSettingsPage;

    private void CollapsePaneForDetail(Type detailPageType)
    {
        if (_paneManagedDetailPageType == null)
        {
            _paneWasOpenBeforeManagedDetail = RootNavigation.IsPaneOpen;
            _managedDetailPaneModified = false;
        }

        _paneManagedDetailPageType = detailPageType;

        if (RootNavigation.IsPaneOpen)
        {
            SetNavigationPaneOpen(false);
        }
    }

    private void RestorePaneAfterManagedDetailIfNeeded()
    {
        if (_paneManagedDetailPageType == null)
        {
            return;
        }

        bool shouldRestorePreviousOpenState =
            _paneWasOpenBeforeManagedDetail &&
            !_managedDetailPaneModified &&
            !RootNavigation.IsPaneOpen;

        _paneManagedDetailPageType = null;
        bool shouldOpenPane = shouldRestorePreviousOpenState;
        _paneWasOpenBeforeManagedDetail = false;
        _managedDetailPaneModified = false;

        if (shouldOpenPane)
        {
            SetNavigationPaneOpen(true);
        }
    }

    private void SetNavigationPaneOpen(bool isOpen)
    {
        if (RootNavigation.IsPaneOpen == isOpen)
        {
            return;
        }

        try
        {
            _isApplyingPaneStateProgrammatically = true;
            RootNavigation.IsPaneOpen = isOpen;
        }
        finally
        {
            _isApplyingPaneStateProgrammatically = false;
        }
    }

    private void RootNavigation_PaneOpened(NavigationView sender, RoutedEventArgs args)
    {
        TrackUserPaneChangeDuringManagedDetail(isOpen: true);
    }

    private void RootNavigation_PaneClosed(NavigationView sender, RoutedEventArgs args)
    {
        TrackUserPaneChangeDuringManagedDetail(isOpen: false);
    }

    private void TrackUserPaneChangeDuringManagedDetail(bool isOpen)
    {
        if (_paneManagedDetailPageType == null || _isApplyingPaneStateProgrammatically)
        {
            return;
        }

        _managedDetailPaneModified = true;
        _paneWasOpenBeforeManagedDetail = isOpen;
    }

    private void AttachTitleBarContextSource(ITitleBarContextSource? source)
    {
        if (ReferenceEquals(_titleBarContextSource, source))
        {
            UpdateTitleBarContext();
            return;
        }

        DetachTitleBarContextSource();
        _titleBarContextSource = source;

        if (_titleBarContextSource != null)
        {
            _titleBarContextSource.TitleBarContextChanged += OnTitleBarContextChanged;
        }

        UpdateTitleBarContext();
    }

    private void DetachTitleBarContextSource()
    {
        if (_titleBarContextSource != null)
        {
            _titleBarContextSource.TitleBarContextChanged -= OnTitleBarContextChanged;
            _titleBarContextSource = null;
        }

        ClearTitleBarContext();
    }

    private void OnTitleBarContextChanged()
    {
        Dispatcher.Invoke(UpdateTitleBarContext);
    }

    private void UpdateTitleBarContext()
    {
        if (_titleBarContextSource == null)
        {
            ClearTitleBarContext();
            return;
        }

        string? title = _titleBarContextSource.TitleBarContextTitle;
        string? statusText = _titleBarContextSource.TitleBarContextStatusText;
        Brush statusBrush = _titleBarContextSource.TitleBarContextStatusBrush
            ?? TryFindBrush("TextFillColorSecondaryBrush", Brushes.Silver);

        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        bool hasStatus = !string.IsNullOrWhiteSpace(statusText);

        TitleBarContextTitle.Text = title ?? string.Empty;
        TitleBarContextTitle.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
        TitleBarContextStatus.Text = statusText ?? string.Empty;
        TitleBarContextStatus.Foreground = statusBrush;
        TitleBarContextStatus.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        TitleBarContextSeparator.Visibility = hasTitle && hasStatus ? Visibility.Visible : Visibility.Collapsed;
        TitleBarContextBorder.Visibility = hasTitle || hasStatus ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearTitleBarContext()
    {
        TitleBarContextTitle.Text = string.Empty;
        TitleBarContextTitle.Visibility = Visibility.Collapsed;
        TitleBarContextStatus.Text = string.Empty;
        TitleBarContextStatus.Visibility = Visibility.Collapsed;
        TitleBarContextSeparator.Visibility = Visibility.Collapsed;
        TitleBarContextBorder.Visibility = Visibility.Collapsed;
    }

    private bool CanNavigateToPage(Type? pageType)
    {
        if (!_isNavigationLockedToRootSetup)
        {
            return true;
        }

        return pageType == typeof(RootDirectorySetupPage);
    }

    private static Type? GetRequestedPageType(object? page)
    {
        if (page is Type pageType)
        {
            return pageType;
        }

        if (page is Page pageInstance)
        {
            return pageInstance.GetType();
        }

        return page?.GetType();
    }

    private void LockNavigationToRootSetup()
    {
        _isNavigationLockedToRootSetup = true;
        _isShowingDetailPage = false;
        DetachTitleBarContextSource();
        RootNavigation.IsPaneVisible = false;
        RootNavigation.IsPaneToggleVisible = false;
        SetNavigationPaneOpen(false);
        SetShellNavigationEnabled(false);
        ClearNavigationSelection();
        BreadcrumbHost.Visibility = Visibility.Collapsed;
        GlobalHealthBorder.Visibility = Visibility.Collapsed;
        UpdateBreadcrumbLabel(null);
    }

    private void UnlockNavigationAfterRootSetup()
    {
        _isNavigationLockedToRootSetup = false;
        RootNavigation.IsPaneVisible = true;
        RootNavigation.IsPaneToggleVisible = true;
        SetShellNavigationEnabled(true);
        BreadcrumbHost.Visibility = Visibility.Visible;
        GlobalHealthBorder.Visibility = Visibility.Visible;
        UpdateBreadcrumbLabel(null);
    }

    private void SetShellNavigationEnabled(bool isEnabled)
    {
        NavDashboard.IsEnabled = isEnabled;
        NavTunnel.IsEnabled = isEnabled;
        NavJavaSetup.IsEnabled = isEnabled;
        NavAbout.IsEnabled = isEnabled;
        NavSettings.IsEnabled = isEnabled;
    }

    // ──────────────────────────────────────────────
    //  Win10 Fallback Mica
    // ──────────────────────────────────────────────

    private void ApplyWin10MicaFallback()
    {
        if (WallpaperMicaService.IsWindows11OrLater || !_applicationState.Settings.EnableMicaEffect)
            return;

        var w = (int)Math.Max(ActualWidth, SystemParameters.PrimaryScreenWidth);
        var h = (int)Math.Max(ActualHeight, SystemParameters.PrimaryScreenHeight);

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var bg = WallpaperMicaService.CreateMicaBackground(
                    targetWidth: w,
                    targetHeight: h,
                    blurRadius: 80,
                    tintOpacity: 0.78,
                    tintColor: Color.FromRgb(32, 32, 32));

                Dispatcher.Invoke(() =>
                {
                    if (bg != null)
                    {
                        MicaFallbackBackground.Source = bg;
                        MicaFallbackBackground.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Win10 Mica fallback failed.");
            }
        });
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop)
            ApplyWin10MicaFallback();
    }

    public void RequestMicaUpdate()
    {
        bool enableMica = _applicationState.Settings.EnableMicaEffect;

        if (WallpaperMicaService.IsWindows11OrLater)
        {
            WindowBackdropType = enableMica 
                ? Wpf.Ui.Controls.WindowBackdropType.Mica 
                : Wpf.Ui.Controls.WindowBackdropType.None;
        }
        else
        {
            if (enableMica)
            {
                ApplyWin10MicaFallback();
            }
            else
            {
                MicaFallbackBackground.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Global Health Monitor
    // ──────────────────────────────────────────────

    private void UpdateGlobalHealth()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                GlobalResourceSummary summary = _globalMonitor.CurrentSummary;
                GlobalHealthTextBlock.Text = summary.DisplayText;

                GlobalHealthTextBlock.Foreground = summary.IsHighUsage
                    ? Brushes.Red
                    : TryFindBrush("TextFillColorSecondaryBrush", Brushes.Silver);
            }
            catch
            {
                // Window may be closing or not fully initialized
            }
        });
    }

    private Brush TryFindBrush(string resourceKey, Brush fallback)
    {
        try
        {
            if (FindResource(resourceKey) is Brush brush)
                return brush;
        }
        catch { }
        return fallback;
    }

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!WallpaperMicaService.IsWindows11OrLater)
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _playitAgentService.OnClaimUrlReceived -= OnPlayitClaimUrlReceived;
        _playitAgentService.OnTunnelRunning -= OnPlayitTunnelRunning;
        RootNavigation.Navigating -= OnNavigating;
        RootNavigation.PaneOpened -= RootNavigation_PaneOpened;
        RootNavigation.PaneClosed -= RootNavigation_PaneClosed;
        DetachTitleBarContextSource();
        _backupScheduler.Stop();
        _globalMonitor.OnGlobalMetricsUpdated -= UpdateGlobalHealth;
        _serverProcessManager.KillAll();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Don't apply Mica early unless explicitly loading settings first.
        // It will be applied during ContinueStartupFlow.

        try
        {
            AppSettings settings = _settingsManager.Load();
            if (string.IsNullOrWhiteSpace(settings.AppRootPath))
            {
                ShowRootDirectorySetupPage();
                return;
            }

            ContinueStartupFlow(settings);
        }
        catch (Exception ex)
        {
            HandleStartupFailure(ex);
        }
    }

    private void ShowRootDirectorySetupPage()
    {
        LockNavigationToRootSetup();

        var setupPage = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<RootDirectorySetupPage>(_serviceProvider);
        setupPage.DirectorySelected += OnRootDirectorySelected;
        setupPage.Unloaded += RootDirectorySetupPage_Unloaded;

        if (!RootNavigation.ReplaceContent(setupPage, null))
        {
            throw new InvalidOperationException("PocketMC could not open the root directory setup page.");
        }
    }

    private void RootDirectorySetupPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RootDirectorySetupPage setupPage)
        {
            return;
        }

        setupPage.DirectorySelected -= OnRootDirectorySelected;
        setupPage.Unloaded -= RootDirectorySetupPage_Unloaded;
    }

    private void OnRootDirectorySelected(object? sender, string rootPath)
    {
        if (sender is RootDirectorySetupPage setupPage)
        {
            setupPage.DirectorySelected -= OnRootDirectorySelected;
            setupPage.Unloaded -= RootDirectorySetupPage_Unloaded;
        }

        try
        {
            var settings = _settingsManager.Load();
            settings.AppRootPath = rootPath;

            Directory.CreateDirectory(rootPath);
            _settingsManager.Save(settings);
            ContinueStartupFlow(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist the PocketMC root directory selection.");
            System.Windows.MessageBox.Show(
                $"PocketMC could not save the selected root folder.\n\n{ex.Message}",
                "Root Folder Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void ContinueStartupFlow(AppSettings settings)
    {
        UnlockNavigationAfterRootSetup();
        _applicationState.ApplySettings(settings);
        
        RequestMicaUpdate();

        if (!_startupServicesStarted)
        {
            _backupScheduler.Start();
            _javaProvisioningService.StartBackgroundProvisioning();
            
            // On first launch, start downloading Playit in the background.
            if (!settings.HasCompletedFirstLaunch)
            {
                _ = _playitAgentService.DownloadAgentAsync();
            }

            _startupServicesStarted = true;
        }

        if (!settings.HasCompletedFirstLaunch)
        {
            NavigateToShellPage(typeof(TunnelPage));
        }
        else
        {
            NavigateToDashboard();
        }

        if (!_playitStartupAttempted)
        {
            _playitStartupAttempted = true;
            TryStartPlayitAgentOnLaunch();
        }
    }

    private void TryStartPlayitAgentOnLaunch()
    {
        try
        {
            if (!File.Exists(_applicationState.GetPlayitExecutablePath()))
            {
                _logger.LogInformation("Playit agent binary is missing; startup auto-connect was skipped.");
                return;
            }

            _playitAgentService.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playit auto-connect failed during app startup. The user can retry from the Tunnel page.");
        }
    }

    private void OnPlayitClaimUrlReceived(object? sender, string claimUrl)
    {
        Dispatcher.Invoke(() =>
        {
            bool navigateToDashboardOnCompletion = !_settingsManager.Load().HasCompletedFirstLaunch;
            var guidePage = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<PocketMC.Desktop.Views.PlayitGuidePage>(_serviceProvider, claimUrl, navigateToDashboardOnCompletion);
            NavigateToDetailPage(guidePage, "Playit.gg Setup");
        });
    }

    private void OnPlayitTunnelRunning(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AppSettings settings = _settingsManager.Load();
            if (settings.HasCompletedFirstLaunch)
            {
                return;
            }

            settings.HasCompletedFirstLaunch = true;
            _settingsManager.Save(settings);
            _applicationState.ApplySettings(settings);
            NavigateToDashboard();
        });
    }

    private void HandleStartupFailure(Exception ex)
    {
        _logger.LogError(ex, "Failed to initialize the PocketMC startup flow.");
        System.Windows.MessageBox.Show(
            "PocketMC could not initialize the main workflow. Check the debug log for details.",
            "Initialization Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
        Application.Current.Shutdown();
    }

}
