using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PocketMC.Desktop.Infrastructure;

/// <summary>
/// Provides helper methods for enabling reliable, 100% controlled mouse wheel scrolling
/// in WPF ScrollViewer controls. Intercepts mouse wheel events on page containers and
/// instantly forwards them to the target ScrollViewer with zero lag or glitching.
/// </summary>
public static class ScrollViewerHelper
{
    private static readonly DependencyProperty MouseWheelHandlerProperty =
        DependencyProperty.RegisterAttached(
            "MouseWheelHandler",
            typeof(MouseWheelEventHandler),
            typeof(ScrollViewerHelper),
            new PropertyMetadata(null));

    /// <summary>
    /// Attaches mouse wheel scrolling support to a Page or UserControl.
    /// Intercepts mouse wheel events and forwards them with 100% instantaneous control.
    /// </summary>
    public static void EnableMouseWheelScrolling(FrameworkElement page, ScrollViewer scrollViewer)
    {
        if (page == null || scrollViewer == null)
            return;

        DisableMouseWheelScrolling(page);

        bool isForwarding = false;
        MouseWheelEventHandler handler = (s, e) =>
        {
            if (isForwarding || e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            if (ShouldSkipWheelForwarding(source, scrollViewer, e.Delta))
            {
                return;
            }

            if (scrollViewer.ScrollableHeight <= 0 || !CanScroll(scrollViewer, e.Delta))
            {
                return;
            }

            e.Handled = true;

            try
            {
                isForwarding = true;
                ScrollByWheelDelta(scrollViewer, e.Delta);
            }
            finally
            {
                isForwarding = false;
            }
        };

        page.SetValue(MouseWheelHandlerProperty, handler);
        page.AddHandler(UIElement.PreviewMouseWheelEvent, handler, true);
    }

    /// <summary>
    /// Detaches mouse wheel scrolling support from a Page or UserControl.
    /// </summary>
    public static void DisableMouseWheelScrolling(FrameworkElement page)
    {
        if (page == null)
            return;

        if (page.GetValue(MouseWheelHandlerProperty) is MouseWheelEventHandler handler)
        {
            page.RemoveHandler(UIElement.PreviewMouseWheelEvent, handler);
            page.ClearValue(MouseWheelHandlerProperty);
        }
    }

    /// <summary>
    /// Disables shell or navigation host ScrollViewer ancestors so pages with their own
    /// ScrollViewer receive a finite height and can scroll independently.
    /// </summary>
    public static void DisableAncestorScrollViewers(DependencyObject element)
    {
        DependencyObject? current = GetParent(element);
        while (current != null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            current = GetParent(current);
        }
    }

    /// <summary>
    /// Attaches mouse wheel scrolling directly to a standalone ScrollViewer.
    /// </summary>
    public static void EnableScrollViewerPreviewWheel(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null)
            return;

        EnableMouseWheelScrolling(scrollViewer, scrollViewer);
    }

    private static bool ShouldSkipWheelForwarding(DependencyObject source, ScrollViewer targetScrollViewer, int delta)
    {
        if (FindAncestor<ScrollBar>(source) != null)
            return true;

        if (FindAncestor<Popup>(source) != null)
            return true;

        if (FindAncestor<ComboBox>(source) is { IsDropDownOpen: true })
            return true;

        if (FindAncestor<TextBox>(source) is { AcceptsReturn: true } textBox &&
            textBox.VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible)
            return true;

        ScrollViewer? nearestScrollViewer = FindAncestor<ScrollViewer>(source);
        return nearestScrollViewer != null &&
               !ReferenceEquals(nearestScrollViewer, targetScrollViewer) &&
               CanScroll(nearestScrollViewer, delta);
    }

    private static bool CanScroll(ScrollViewer scrollViewer, int delta)
    {
        if (delta > 0)
            return scrollViewer.VerticalOffset > 0;

        if (delta < 0)
            return scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

        return false;
    }

    /// <summary>
    /// Scrolls the ScrollViewer by the specified wheel delta instantly with 100% direct control.
    /// </summary>
    public static void ScrollByWheelDelta(ScrollViewer scrollViewer, int delta)
    {
        int wheelScrollLines = SystemParameters.WheelScrollLines;
        if (wheelScrollLines == 0 || delta == 0)
            return;

        int notches = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (double)Mouse.MouseWheelDeltaForOneLine));

        if (wheelScrollLines < 0)
        {
            if (delta > 0)
                scrollViewer.PageUp();
            else
                scrollViewer.PageDown();

            return;
        }

        int steps = notches * wheelScrollLines;
        for (int i = 0; i < steps; i++)
        {
            if (delta > 0)
                scrollViewer.LineUp();
            else
                scrollViewer.LineDown();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        DependencyObject? visualParent = null;
        try
        {
            visualParent = VisualTreeHelper.GetParent(current);
        }
        catch
        {
            // Some content elements are not in the visual tree.
        }

        return visualParent ?? LogicalTreeHelper.GetParent(current);
    }
}
