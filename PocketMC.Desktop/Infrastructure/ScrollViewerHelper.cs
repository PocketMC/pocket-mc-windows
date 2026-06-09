using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PocketMC.Desktop.Infrastructure;

/// <summary>
/// Provides helper methods for enabling reliable mouse wheel scrolling in WPF ScrollViewer controls.
/// This helper addresses common issues where child controls (ComboBox, TextBox, etc.) consume
/// mouse wheel events before they reach the ScrollViewer.
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
    /// This method registers a handler that intercepts mouse wheel events and forwards them
    /// to the specified ScrollViewer, even if child controls have already handled the event.
    /// </summary>
    /// <param name="page">The Page or UserControl to attach scrolling to.</param>
    /// <param name="scrollViewer">The target ScrollViewer that should receive scroll events.</param>
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
    /// <param name="page">The Page or UserControl to detach scrolling from.</param>
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
    /// Alternative approach: Attaches a PreviewMouseWheel handler to the ScrollViewer itself.
    /// This is less aggressive than the Page-level approach and may work in some scenarios.
    /// </summary>
    /// <param name="scrollViewer">The ScrollViewer to attach the handler to.</param>
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

    private static void ScrollByWheelDelta(ScrollViewer scrollViewer, int delta)
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
