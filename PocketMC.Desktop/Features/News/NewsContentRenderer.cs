using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PocketMC.Infrastructure.News;

namespace PocketMC.Desktop.Features.News;

internal static class NewsContentRenderer
{
    public static StackPanel Build(NewsItem item)
    {
        StackPanel content = new() { Orientation = Orientation.Vertical };
        foreach (NewsContentBlock block in item.Blocks)
        {
            FrameworkElement? element = BuildBlock(block);
            if (element != null) content.Children.Add(element);
        }

        return content;
    }

    private static FrameworkElement? BuildBlock(NewsContentBlock block)
    {
        switch (block.Type)
        {
            case NewsBlockType.Title:
                return Text(block.Text, 26, FontWeights.SemiBold, "TextFillColorPrimaryBrush", 0, 6);
            case NewsBlockType.Subtitle:
                return Text(block.Text, 15, FontWeights.Normal, "TextFillColorSecondaryBrush", 0, 16);
            case NewsBlockType.Heading:
                return Text(block.Text, 17, FontWeights.SemiBold, "TextFillColorPrimaryBrush", 0, 7, 18);
            case NewsBlockType.Paragraph:
                return Text(block.Text, 14, FontWeights.Normal, "TextFillColorPrimaryBrush", 0, 8, 20);
            case NewsBlockType.Warning:
                return Callout(block.Text, "Warning", "SystemFillColorCautionBrush");
            case NewsBlockType.Important:
                return Callout(block.Text, "Important", "AccentTextFillColorPrimaryBrush");
            case NewsBlockType.BulletList:
                return BuildList(block.Items, numbered: false);
            case NewsBlockType.NumberedList:
                return BuildList(block.Items, numbered: true);
            case NewsBlockType.Code:
                return new Border
                {
                    Background = Brush("ControlFillColorSecondaryBrush"),
                    BorderBrush = Brush("ControlStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 4, 0, 12),
                    Child = new TextBox
                    {
                        Text = block.Text,
                        IsReadOnly = true,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Foreground = Brush("TextFillColorPrimaryBrush"),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 13,
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
                    }
                };
            case NewsBlockType.Link:
                return BuildLink(block);
            case NewsBlockType.Divider:
                return new Separator { Margin = new Thickness(0, 8, 0, 12), Opacity = 0.5 };
            default:
                return null;
        }
    }

    private static FrameworkElement BuildList(System.Collections.Generic.IReadOnlyList<string>? items, bool numbered)
    {
        StackPanel list = new() { Margin = new Thickness(0, 0, 0, 12) };
        if (items == null) return list;

        for (int index = 0; index < items.Count; index++)
        {
            Grid row = new() { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock marker = new()
            {
                Text = numbered ? $"{index + 1}." : "•",
                Width = 24,
                Foreground = Brush("AccentTextFillColorPrimaryBrush"),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);
            TextBlock itemText = Text(items[index], 14, FontWeights.Normal, "TextFillColorPrimaryBrush", 0, 0, 20);
            Grid.SetColumn(itemText, 1);
            row.Children.Add(itemText);
            list.Children.Add(row);
        }

        return list;
    }

    private static FrameworkElement BuildLink(NewsContentBlock block)
    {
        Hyperlink hyperlink = new(new Run(block.Text)) { NavigateUri = block.Link };
        hyperlink.RequestNavigate += (_, args) =>
        {
            args.Handled = true;
            OpenSafeLink(args.Uri);
        };
        TextBlock text = new()
        {
            Margin = new Thickness(0, 4, 0, 12),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        text.Inlines.Add(hyperlink);
        return text;
    }

    private static void OpenSafeLink(Uri? uri)
    {
        if (uri == null || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
    }

    private static FrameworkElement Callout(string message, string label, string resourceKey)
    {
        StackPanel body = new();
        body.Children.Add(Text(label, 12, FontWeights.SemiBold, resourceKey, 0, 3));
        body.Children.Add(Text(message, 13, FontWeights.Normal, "TextFillColorPrimaryBrush", 0, 0, 19));
        return new Border
        {
            Background = Brush("CardBackgroundFillColorSecondaryBrush"),
            BorderBrush = Brush(resourceKey),
            BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 5, 0, 12),
            Child = body
        };
    }

    private static TextBlock Text(string value, double fontSize, FontWeight weight, string brushKey, double top, double bottom, double? lineHeight = null)
        => new()
        {
            Text = value,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = Brush(brushKey),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, bottom),
            LineHeight = lineHeight ?? double.NaN
        };

    private static Brush Brush(string key)
        => System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}