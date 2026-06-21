using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PocketMC.Desktop.Features.Shell;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Helpers;

public static class AnimatedNavIndicatorBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty MenuIndicatorBorderProperty =
        DependencyProperty.RegisterAttached(
            "MenuIndicatorBorder",
            typeof(Border),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty FooterIndicatorBorderProperty =
        DependencyProperty.RegisterAttached(
            "FooterIndicatorBorder",
            typeof(Border),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty AccentChangedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "AccentChangedHandler",
            typeof(Action<Color>),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty TargetVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "TargetVisibility",
            typeof(bool),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(false));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView navView)
        {
            if ((bool)e.NewValue)
            {
                navView.Loaded += NavView_Loaded;
                navView.SizeChanged += NavView_SizeChanged;
                AttachAccentChangedHandler(navView);
            }
            else
            {
                navView.Loaded -= NavView_Loaded;
                navView.SizeChanged -= NavView_SizeChanged;
                DetachAccentChangedHandler(navView);
                if (navView.GetValue(MenuIndicatorBorderProperty) is Border menuBorder && menuBorder.Parent is Panel menuPanel)
                {
                    menuPanel.Children.Remove(menuBorder);
                }
                if (navView.GetValue(FooterIndicatorBorderProperty) is Border footerBorder && footerBorder.Parent is Panel footerPanel)
                {
                    footerPanel.Children.Remove(footerBorder);
                }
                navView.ClearValue(MenuIndicatorBorderProperty);
                navView.ClearValue(FooterIndicatorBorderProperty);
            }
        }
    }

    private static void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationView navView)
        {
            EnsureIndicators(navView);
            HideDefaultIndicators(navView);
            AnimateToActiveItem(navView, false); // Snap on load
        }
    }

    private static void NavView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is NavigationView navView)
        {
            // Defer the snap to ensure the visual tree has fully arranged all items after a resize
            navView.Dispatcher.BeginInvoke(new Action(() => 
            {
                HideDefaultIndicators(navView);
                
                var menuIndicator = navView.GetValue(MenuIndicatorBorderProperty) as Border;
                var footerIndicator = navView.GetValue(FooterIndicatorBorderProperty) as Border;
                
                bool isMenuVisible = menuIndicator != null && (bool)menuIndicator.GetValue(TargetVisibilityProperty);
                bool isFooterVisible = footerIndicator != null && (bool)footerIndicator.GetValue(TargetVisibilityProperty);
                
                if (isMenuVisible || isFooterVisible)
                {
                    AnimateToActiveItem(navView, false);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static void EnsureIndicators(NavigationView navView)
    {
        if (navView.GetValue(MenuIndicatorBorderProperty) is Border &&
            navView.GetValue(FooterIndicatorBorderProperty) is Border) return;

        // Find the root visual child
        var rootGrid = FindVisualChild<Grid>(navView);
        if (rootGrid == null) return;

        // The indicator should ideally be added to the ItemsContainerGrid or PaneContentGrid
        var paneGrid = FindChildByName<Grid>(navView, "PaneGrid"); 
        if (paneGrid == null)
        {
            // For Left mode
            paneGrid = FindChildByName<Grid>(navView, "PaneContentGrid");
        }
        if (paneGrid == null)
        {
            // Fallback
            paneGrid = rootGrid;
        }

        if (navView.GetValue(MenuIndicatorBorderProperty) is not Border)
        {
            var menuIndicator = CreateIndicatorBorder();
            paneGrid.Children.Add(menuIndicator);
            if (paneGrid.RowDefinitions.Count > 0)
            {
                Grid.SetRowSpan(menuIndicator, paneGrid.RowDefinitions.Count);
            }
            navView.SetValue(MenuIndicatorBorderProperty, menuIndicator);
        }

        if (navView.GetValue(FooterIndicatorBorderProperty) is not Border)
        {
            var footerIndicator = CreateIndicatorBorder();
            paneGrid.Children.Add(footerIndicator);
            if (paneGrid.RowDefinitions.Count > 0)
            {
                Grid.SetRowSpan(footerIndicator, paneGrid.RowDefinitions.Count);
            }
            navView.SetValue(FooterIndicatorBorderProperty, footerIndicator);
        }
    }

    private static Border CreateIndicatorBorder()
    {
        var indicator = new Border
        {
            Width = 3,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(2),
            Background = ResolveIndicatorBrush(),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Opacity = 0 // Hidden initially
        };

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform { ScaleX = 1, ScaleY = 1 });
        group.Children.Add(new TranslateTransform { X = 0, Y = -100 });
        indicator.RenderTransform = group;

        return indicator;
    }

    private static void AttachAccentChangedHandler(NavigationView navView)
    {
        if (navView.GetValue(AccentChangedHandlerProperty) is Action<Color>) return;

        void Handler(Color _)
        {
            if (!navView.Dispatcher.CheckAccess())
            {
                navView.Dispatcher.BeginInvoke(new Action(() => UpdateIndicatorBrush(navView)));
                return;
            }

            UpdateIndicatorBrush(navView);
        }

        Action<Color> handler = Handler;
        AccentColorService.GlobalAccentChanged += handler;
        navView.SetValue(AccentChangedHandlerProperty, handler);
    }

    private static void DetachAccentChangedHandler(NavigationView navView)
    {
        if (navView.GetValue(AccentChangedHandlerProperty) is Action<Color> handler)
        {
            AccentColorService.GlobalAccentChanged -= handler;
        }

        navView.ClearValue(AccentChangedHandlerProperty);
    }

    private static void UpdateIndicatorBrush(NavigationView navView)
    {
        if (navView.GetValue(MenuIndicatorBorderProperty) is Border menuIndicator)
        {
            menuIndicator.Background = ResolveIndicatorBrush();
        }
        if (navView.GetValue(FooterIndicatorBorderProperty) is Border footerIndicator)
        {
            footerIndicator.Background = ResolveIndicatorBrush();
        }
    }

    private static Brush ResolveIndicatorBrush()
    {
        Brush? brush = Application.Current?.TryFindResource("NavigationViewSelectionIndicatorForeground") as Brush;
        return brush ?? Brushes.DodgerBlue;
    }

    public static void AnimateToActiveItem(NavigationView navView, bool animate = true)
    {
        EnsureIndicators(navView);

        var menuIndicator = navView.GetValue(MenuIndicatorBorderProperty) as Border;
        var footerIndicator = navView.GetValue(FooterIndicatorBorderProperty) as Border;
        if (menuIndicator == null || footerIndicator == null) return;

        // Find the active item
        var activeItem = FindActiveItem(navView);
        if (activeItem == null)
        {
            FadeOutIndicator(menuIndicator, animate);
            FadeOutIndicator(footerIndicator, animate);
            return;
        }

        // Hide default indicator inside this item specifically
        HideDefaultIndicator(activeItem);

        // Determine which indicator is active and which is inactive
        bool isFooter = IsFooterItem(navView, activeItem);
        var activeIndicator = isFooter ? footerIndicator : menuIndicator;
        var inactiveIndicator = isFooter ? menuIndicator : footerIndicator;

        // Fade out the inactive indicator
        FadeOutIndicator(inactiveIndicator, animate);

        // Position and animate/fade in the active indicator
        var container = VisualTreeHelper.GetParent(activeIndicator) as UIElement;
        if (container == null) return;

        try
        {
            var transform = activeItem.TransformToAncestor(container);
            var activeItemRect = transform.TransformBounds(new Rect(0, 0, activeItem.ActualWidth, activeItem.ActualHeight));

            double targetY = activeItemRect.Top + (activeItemRect.Height / 2) - (activeIndicator.Height / 2);

            var group = activeIndicator.RenderTransform as TransformGroup;
            if (group == null)
            {
                group = new TransformGroup();
                group.Children.Add(new ScaleTransform { ScaleX = 1, ScaleY = 1 });
                group.Children.Add(new TranslateTransform { X = 0, Y = 0 });
                activeIndicator.RenderTransformOrigin = new Point(0.5, 0.5);
                activeIndicator.RenderTransform = group;
            }

            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];

            bool isNewlyVisible = !(bool)activeIndicator.GetValue(TargetVisibilityProperty);

            // Ensure the active indicator fades in
            FadeInIndicator(activeIndicator, animate);

            if (isNewlyVisible || !animate)
            {
                // Snap to target immediately without sliding animation
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                translate.Y = targetY;
                scale.ScaleY = 1;
                return;
            }

            // Calculate distance to determine how much to stretch
            double currentY = translate.Y;
            double distance = Math.Abs(targetY - currentY);
            
            // Max stretch is 2.5x, scaling up based on distance
            double stretchFactor = 1.0;
            if (distance > 10)
            {
                stretchFactor = Math.Min(2.5, 1.0 + (distance / 50.0));
            }

            var moveAnim = new DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            
            var stretchAnim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(250)
            };
            stretchAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            stretchAnim.KeyFrames.Add(new EasingDoubleKeyFrame(stretchFactor, KeyTime.FromPercent(0.5)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            stretchAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

            // Set the base value so next distance calculation is somewhat grounded
            translate.Y = targetY;
            translate.BeginAnimation(TranslateTransform.YProperty, moveAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, stretchAnim);
        }
        catch (Exception)
        {
            // Ignore transformation errors during layout updates
        }
    }

    private static void FadeInIndicator(Border indicator, bool animate)
    {
        bool isTargetVisible = (bool)indicator.GetValue(TargetVisibilityProperty);
        if (isTargetVisible && indicator.HasAnimatedProperties) return;

        indicator.SetValue(TargetVisibilityProperty, true);

        if (animate)
        {
            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            indicator.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        else
        {
            indicator.BeginAnimation(UIElement.OpacityProperty, null);
            indicator.Opacity = 1;
        }
    }

    private static void FadeOutIndicator(Border indicator, bool animate)
    {
        bool isTargetVisible = (bool)indicator.GetValue(TargetVisibilityProperty);
        if (!isTargetVisible && indicator.HasAnimatedProperties) return;

        indicator.SetValue(TargetVisibilityProperty, false);

        if (animate)
        {
            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            indicator.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            indicator.BeginAnimation(UIElement.OpacityProperty, null);
            indicator.Opacity = 0;
        }
    }

    private static bool IsFooterItem(NavigationView navView, NavigationViewItem item)
    {
        if (navView.FooterMenuItems != null)
        {
            foreach (var footerObj in navView.FooterMenuItems)
            {
                if (ReferenceEquals(footerObj, item)) return true;
                if (footerObj is NavigationViewItem footerItem && IsChildOf(footerItem, item)) return true;
            }
        }
        return false;
    }

    private static bool IsChildOf(NavigationViewItem parent, NavigationViewItem child)
    {
        if (parent.MenuItems != null)
        {
            foreach (var subObj in parent.MenuItems)
            {
                if (ReferenceEquals(subObj, child)) return true;
                if (subObj is NavigationViewItem subItem && IsChildOf(subItem, child)) return true;
            }
        }
        return false;
    }

    private static NavigationViewItem? FindActiveItem(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is NavigationViewItem item && item.IsActive && item.IsVisible)
            {
                return item;
            }
            var result = FindActiveItem(child);
            if (result != null) return result;
        }
        return null;
    }

    private static void HideDefaultIndicators(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is NavigationViewItem item)
            {
                HideDefaultIndicator(item);
            }
            HideDefaultIndicators(child);
        }
    }

    private static void HideDefaultIndicator(NavigationViewItem item)
    {
        var rect = FindChildByName<Rectangle>(item, "ActiveRectangle");
        if (rect != null && rect.Visibility != Visibility.Collapsed)
        {
            rect.Visibility = Visibility.Collapsed;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child != null && child is T t)
                return t;
            else
            {
                var childOfChild = FindVisualChild<T>(child!);
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
    }

    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.Name == name)
                return t;
            else
            {
                var childOfChild = FindChildByName<T>(child, name);
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
    }
}
