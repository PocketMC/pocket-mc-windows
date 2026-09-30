using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Infrastructure.News;

namespace PocketMC.Desktop.Features.News;

public partial class NewsArticleView : UserControl
{
    public NewsArticleView()
    {
        InitializeComponent();
    }

    public void Display(NewsItem item)
    {
        ArticleBadges.Children.Clear();
        ArticleContent.Children.Clear();

        AddBadge(Format(item.Metadata.Type));
        AddBadge(Format(item.Metadata.Priority));
        AddBadge(item.Metadata.PublishedUtc.ToLocalTime().ToString("g"));
        ArticleContent.Children.Add(NewsContentRenderer.Build(item));
    }

    private void AddBadge(string text)
    {
        TextBlock label = new()
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        Border badge = new()
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 5, 3),
            Child = label
        };
        badge.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        badge.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        ArticleBadges.Children.Add(badge);
    }

    private static string Format<T>(T value) where T : Enum
        => Regex.Replace(value.ToString(), "(?<!^)([A-Z])", " $1");
}