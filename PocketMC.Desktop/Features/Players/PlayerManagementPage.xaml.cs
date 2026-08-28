using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Players;

public partial class PlayerManagementPage : Page, IDisposable, ITitleBarContextSource
{
    private readonly MouseWheelEventHandler _previewMouseWheelHandler;
    private bool _isDisposed;

    public PlayerManagementPage(PlayerManagementViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        _previewMouseWheelHandler = OnPagePreviewMouseWheel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
    }

    public PlayerManagementViewModel ViewModel { get; }
    public string? TitleBarContextTitle => ViewModel.InstanceName;
    public string? TitleBarContextStatusText => ViewModel.ServerStatusText;
    public Brush? TitleBarContextStatusBrush => ViewModel.ServerStatusBrush;
    public event Action? TitleBarContextChanged;

    private bool _isFirstLoad = true;
    private bool _allowTabAnimation = false;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AddHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler, true);

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            if (SidebarList.MenuItems.Count > 0 && SidebarList.MenuItems[0] is Wpf.Ui.Controls.NavigationViewItem firstItem)
            {
                firstItem.IsActive = true;
                MainTabControl.SelectedIndex = 0;
            }

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => _allowTabAnimation = true));
        }
        else
        {
            QueueTabTransitionAnimation();
        }

        if (Window.GetWindow(this) is Shell.MainWindow mainWindow)
        {
            mainWindow.RootNavigation.IsPaneOpen = false;
            DisableParentScrollViewer(this);
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);

        if (Window.GetWindow(this) is Shell.MainWindow mainWindow)
        {
            mainWindow.RootNavigation.IsPaneOpen = true;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerManagementViewModel.ServerStatusText) or nameof(PlayerManagementViewModel.ServerStatusBrush))
        {
            TitleBarContextChanged?.Invoke();
        }
    }

    // ── Sidebar ↔ TabControl synchronization ────────────────────────────────

    private bool _isSynchronizingTabSelection;

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingTabSelection) return;

        if (sender is Wpf.Ui.Controls.NavigationViewItem clickedItem && MainTabControl != null)
        {
            int idx = SidebarList.MenuItems.IndexOf(clickedItem);
            if (idx != -1 && MainTabControl.SelectedIndex != idx)
            {
                MainTabControl.SelectedIndex = idx;

                foreach (var item in SidebarList.MenuItems)
                {
                    if (item is Wpf.Ui.Controls.NavigationViewItem navItem)
                        navItem.IsActive = false;
                }
                clickedItem.IsActive = true;
            }
        }
    }

    private void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl) return;

        int sideIndex = -1;
        for (int i = 0; i < SidebarList.MenuItems.Count; i++)
        {
            if (SidebarList.MenuItems[i] is Wpf.Ui.Controls.NavigationViewItem item && item.IsActive)
            {
                sideIndex = i;
                break;
            }
        }
        if (sideIndex != MainTabControl.SelectedIndex)
        {
            _isSynchronizingTabSelection = true;
            if (MainTabControl.SelectedIndex >= 0 && MainTabControl.SelectedIndex < SidebarList.MenuItems.Count)
            {
                foreach (var item in SidebarList.MenuItems)
                {
                    if (item is Wpf.Ui.Controls.NavigationViewItem navItem)
                        navItem.IsActive = false;
                }

                if (SidebarList.MenuItems[MainTabControl.SelectedIndex] is Wpf.Ui.Controls.NavigationViewItem targetItem)
                {
                    targetItem.IsActive = true;
                }
            }
            _isSynchronizingTabSelection = false;
        }

        // Trigger whitelist load when switching to the Whitelist tab
        if (MainTabControl.SelectedIndex == 2)
        {
            _ = ViewModel.LoadWhitelistAsync();
        }

        QueueTabTransitionAnimation();
    }

    // ── Tab transition animation ──────────────────────────────────────────

    private void QueueTabTransitionAnimation()
    {
        if (!IsLoaded || !_allowTabAnimation) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(AnimateContentAreaTransition));
    }

    private void AnimateContentAreaTransition()
    {
        if (ContentAreaCard == null) return;

        ContentAreaCard.BeginAnimation(OpacityProperty, null);
        if (ContentAreaCard.RenderTransform is TranslateTransform translateTransform)
        {
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            translateTransform.Y = 8;
            translateTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        ContentAreaCard.Opacity = 0;
        ContentAreaCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }

    // ── Mouse wheel forwarding ────────────────────────────────────────────

    private bool _isForwardingMouseWheel;
    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isForwardingMouseWheel || e.OriginalSource is not DependencyObject source) return;

        if (FindAncestor<ScrollBar>(source) != null) return;

        ComboBox? comboBox = FindAncestor<ComboBox>(source);
        if (comboBox?.IsDropDownOpen == true) return;

        ScrollViewer? activeScrollViewer = GetActiveTabScrollViewer();
        if (activeScrollViewer == null || activeScrollViewer.ScrollableHeight <= 0) return;

        e.Handled = true;

        try
        {
            _isForwardingMouseWheel = true;
            int steps = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine) * 3;
            for (int i = 0; i < steps; i++)
            {
                if (e.Delta > 0) activeScrollViewer.LineUp();
                else activeScrollViewer.LineDown();
            }
        }
        finally
        {
            _isForwardingMouseWheel = false;
        }
    }

    private ScrollViewer? GetActiveTabScrollViewer()
    {
        return MainTabControl.SelectedIndex switch
        {
            0 => OnlineScrollViewer,
            1 => BanListScrollViewer,
            2 => WhitelistScrollViewer,
            _ => null
        };
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            DependencyObject? visualParent = null;
            try { visualParent = VisualTreeHelper.GetParent(current); } catch { }
            current = visualParent ?? LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void DisableParentScrollViewer(DependencyObject obj)
    {
        var parent = VisualTreeHelper.GetParent(obj);
        while (parent != null)
        {
            if (parent is ScrollViewer sv)
            {
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
    }
}
