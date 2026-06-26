using System;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Intelligence
{
    /// <summary>
    /// WPF-native markdown viewer using FlowDocumentScrollViewer.
    /// No WebView2, no external browser processes, zero memory overhead.
    /// Works reliably in any WPF container state (Collapsed, Visible, Tab, etc.)
    /// </summary>
    public partial class NativeMarkdownViewer : UserControl
    {
        public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(NativeMarkdownViewer),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        public NativeMarkdownViewer()
        {
            InitializeComponent();
        }

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NativeMarkdownViewer viewer)
            {
                viewer.RenderMarkdown(e.NewValue as string ?? string.Empty);
            }
        }

        private void RenderMarkdown(string rawMarkdown)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawMarkdown))
                {
                    DocumentViewer.Document = null;
                    return;
                }

                bool isDarkMode = true;
                try
                {
                    isDarkMode = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
                                 == Wpf.Ui.Appearance.ApplicationTheme.Dark;
                }
                catch
                {
                    // Fallback to dark
                }

                var doc = MarkdownFlowDocumentConverter.Convert(rawMarkdown, isDarkMode);
                DocumentViewer.Document = doc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NativeMarkdownViewer render error: {ex.Message}");
            }
        }
        private ScrollViewer? _internalScrollViewer;

        private void DocumentViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (e.Handled) return;

            _internalScrollViewer ??= FindVisualChild<ScrollViewer>(DocumentViewer);

            if (_internalScrollViewer != null)
            {
                e.Handled = true;
                
                // Mouse.MouseWheelDeltaForOneLine is usually 120
                int steps = System.Math.Max(1, System.Math.Abs(e.Delta) / 120) * 3;
                for (int i = 0; i < steps; i++)
                {
                    if (e.Delta > 0) _internalScrollViewer.LineUp();
                    else _internalScrollViewer.LineDown();
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }
    }
}
