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
        SyncNavigationSelection(typeof(DashboardPage));

        // Listen for navigation events to update breadcrumb
        RootNavigation.Navigated += OnNavigated;
        RootNavigation.PaneOpened += RootNavigation_PaneOpened;
        RootNavigation.PaneClosed += RootNavigation_PaneClosed;

        Closing += MainWindow_Closing;
        _globalMonitor.OnGlobalMetricsUpdated += UpdateGlobalHealth;
        _playitAgentService.OnClaimUrlReceived += OnPlayitClaimUrlReceived;

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
        pageType == typeof(JavaSetupPage);

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
        return ReplaceShellContent(typeof(DashboardPage));
    }

    public bool NavigateToDetailPage(Page page, string breadcrumbLabel)
    {
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
        if (RootNavigation.CanGoBack && RootNavigation.GoBack())
        {
            return true;
        }

        return NavigateToShellPage(_lastShellPageType);
    }

    private bool ReplaceShellContent(Type pageType)
    {
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

    // ──────────────────────────────────────────────
    //  Win10 Fallback Mica
    // ──────────────────────────────────────────────

    private void ApplyWin10MicaFallback()
    {
        if (WallpaperMicaService.IsWindows11OrLater)
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
        RootNavigation.PaneOpened -= RootNavigation_PaneOpened;
        RootNavigation.PaneClosed -= RootNavigation_PaneClosed;
        DetachTitleBarContextSource();
        _backupScheduler.Stop();
        _globalMonitor.OnGlobalMetricsUpdated -= UpdateGlobalHealth;
        _serverProcessManager.KillAll();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWin10MicaFallback();

        var settings = _settingsManager.Load();

        if (string.IsNullOrEmpty(settings.AppRootPath))
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select First-Run Root Folder for PocketMC",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                settings.AppRootPath = dialog.FolderName;
                _settingsManager.Save(settings);
            }
            else
            {
                Application.Current.Shutdown();
                return;
            }
        }

        _applicationState.ApplySettings(settings);
        _backupScheduler.Start();
        _javaProvisioningService.StartBackgroundProvisioning();

        try
        {
            if (!settings.HasCompletedFirstLaunch)
            {
                settings.HasCompletedFirstLaunch = true;
                _settingsManager.Save(settings);

                // If no playit config exists, route to Tunnel Page for first-time setup
                string configPath = _settingsManager.GetPlayitTomlPath(settings);
                if (!System.IO.File.Exists(configPath))
                {
                    ReplaceShellContent(typeof(PocketMC.Desktop.Views.TunnelPage));
                }
                else
                {
                    NavigateToDashboard();
                }
            }
            else
            {
                NavigateToDashboard();
            }

            TryStartPlayitAgentOnLaunch();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to the Dashboard page.");
            System.Windows.MessageBox.Show(
                "PocketMC could not initialize the main workflow. Check the debug log for details.",
                "Initialization Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Application.Current.Shutdown();
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
            var guidePage = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<PocketMC.Desktop.Views.PlayitGuidePage>(_serviceProvider, claimUrl);
            NavigateToDetailPage(guidePage, "Playit.gg Setup");
        });
    }

}
