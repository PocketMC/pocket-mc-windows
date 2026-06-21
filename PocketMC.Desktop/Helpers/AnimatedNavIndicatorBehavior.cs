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

    private static readonly DependencyProperty IndicatorBorderProperty =
        DependencyProperty.RegisterAttached(
            "IndicatorBorder",
            typeof(Border),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty AccentChangedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "AccentChangedHandler",
            typeof(Action<Color>),
            typeof(AnimatedNavIndicatorBehavior),
            new PropertyMetadata(null));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView navView)
        {
            if ((bool)e.NewValue)
            {
                navView.Loaded += NavView_Loaded;
                navView.LayoutUpdated += NavView_LayoutUpdated;
                AttachAccentChangedHandler(navView);
            }
            else
            {
                navView.Loaded -= NavView_Loaded;
                navView.LayoutUpdated -= NavView_LayoutUpdated;
                DetachAccentChangedHandler(navView);
                if (navView.GetValue(IndicatorBorderProperty) is Border border && border.Parent is Panel panel)
                {
                    panel.Children.Remove(border);
                }
                navView.ClearValue(IndicatorBorderProperty);
            }
        }
    }

    private static void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationView navView)
        {
            EnsureIndicator(navView);
            HideDefaultIndicators(navView);
            AnimateToActiveItem(navView, false); // Snap on load
        }
    }

    private static void NavView_LayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is NavigationView navView)
        {
            HideDefaultIndicators(navView);
            
            // If the layout changed (e.g., resizing the window or toggling pane),
            // we should snap the indicator to its proper place without animation 
            // if it's already visible, to prevent it from detaching from the item.
            var indicator = navView.GetValue(IndicatorBorderProperty) as Border;
            if (indicator != null && indicator.Opacity > 0)
            {
                AnimateToActiveItem(navView, false);
            }
        }
    }

    private static void EnsureIndicator(NavigationView navView)
    {
        if (navView.GetValue(IndicatorBorderProperty) is Border) return;

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

        // Add to the grid
        paneGrid.Children.Add(indicator);
        
        if (paneGrid.RowDefinitions.Count > 0)
        {
            Grid.SetRowSpan(indicator, paneGrid.RowDefinitions.Count);
        }

        navView.SetValue(IndicatorBorderProperty, indicator);
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
        if (navView.GetValue(IndicatorBorderProperty) is Border indicator)
        {
            indicator.Background = ResolveIndicatorBrush();
        }
    }

    private static Brush ResolveIndicatorBrush()
    {
        Brush? brush = Application.Current?.TryFindResource("NavigationViewSelectionIndicatorForeground") as Brush;
        return brush ?? Brushes.DodgerBlue;
    }

    public static void AnimateToActiveItem(NavigationView navView, bool animate = true)
    {
        var indicator = navView.GetValue(IndicatorBorderProperty) as Border;
        if (indicator == null)
        {
            EnsureIndicator(navView);
            indicator = navView.GetValue(IndicatorBorderProperty) as Border;
            if (indicator == null) return;
        }

        // Find the active item
        var activeItem = FindActiveItem(navView);
        if (activeItem == null)
        {
            indicator.Opacity = 0;
            return;
        }

        // Hide default indicator inside this item specifically
        HideDefaultIndicator(activeItem);

        // Calculate position relative to the container of the indicator
        var container = VisualTreeHelper.GetParent(indicator) as UIElement;
        if (container == null) return;

        try
        {
            var transform = activeItem.TransformToAncestor(container);
            var activeItemRect = transform.TransformBounds(new Rect(0, 0, activeItem.ActualWidth, activeItem.ActualHeight));

            double targetY = activeItemRect.Top + (activeItemRect.Height / 2) - (indicator.Height / 2);

            var group = indicator.RenderTransform as TransformGroup;
            if (group == null)
            {
                group = new TransformGroup();
                group.Children.Add(new ScaleTransform { ScaleX = 1, ScaleY = 1 });
                group.Children.Add(new TranslateTransform { X = 0, Y = 0 });
                indicator.RenderTransformOrigin = new Point(0.5, 0.5);
                indicator.RenderTransform = group;
            }

            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];

            if (indicator.Opacity == 0)
            {
                translate.Y = targetY;
                scale.ScaleY = 1;
                indicator.Opacity = 1;
                return;
            }

            if (!animate)
            {
                // Only snap if we aren't currently animating
                if (!translate.HasAnimatedProperties)
                {
                    translate.Y = targetY;
                }
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

            translate.BeginAnimation(TranslateTransform.YProperty, moveAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, stretchAnim);
        }
        catch (Exception)
        {
            // Ignore transformation errors during layout updates
        }
    }

    private static NavigationViewItem? FindActiveItem(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is NavigationViewItem item && item.IsActive)
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
