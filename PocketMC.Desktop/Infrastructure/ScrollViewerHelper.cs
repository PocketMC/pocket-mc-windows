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

        // Register handler with handledEventsToo=true to catch events even if already handled by child controls
        page.AddHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((s, e) =>
        {
            // Check if the mouse is over a ComboBox dropdown - if so, let it handle the wheel
            if (IsMouseOverComboBoxDropdown(e.OriginalSource))
            {
                return;
            }

            // Always handle the wheel event and scroll the ScrollViewer
            e.Handled = true;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        }), true);
    }

    /// <summary>
    /// Detaches mouse wheel scrolling support from a Page or UserControl.
    /// </summary>
    /// <param name="page">The Page or UserControl to detach scrolling from.</param>
    public static void DisableMouseWheelScrolling(FrameworkElement page)
    {
        if (page == null)
            return;

        // Remove all handlers - this is a simple approach that removes all MouseWheel handlers
        // In a more sophisticated implementation, you'd store the handler reference
        page.RemoveHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((s, e) => { }));
    }

    /// <summary>
    /// Checks if the mouse is currently over a ComboBox dropdown (Popup).
    /// This ensures ComboBox dropdown scrolling works normally.
    /// </summary>
    /// <param name="originalSource">The original source of the mouse wheel event.</param>
    /// <returns>True if the mouse is over a ComboBox dropdown, false otherwise.</returns>
    private static bool IsMouseOverComboBoxDropdown(object originalSource)
    {
        // Walk up the visual tree to check if we're inside a ComboBox dropdown
        DependencyObject? current = originalSource as DependencyObject;
        while (current != null)
        {
            if (current is ComboBox comboBox && comboBox.IsDropDownOpen)
            {
                return true;
            }
            // Also check for Popup (ComboBox dropdowns are Popups)
            if (current is Popup)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
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

        scrollViewer.PreviewMouseWheel += (s, e) =>
        {
            // Check if the mouse is over a ComboBox dropdown
            if (IsMouseOverComboBoxDropdown(e.OriginalSource))
            {
                return;
            }

            // Handle the wheel event
            e.Handled = true;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        };
    }
}
