using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.News;

namespace PocketMC.Desktop.Features.News;

public partial class NewsPage : Page
{
    private readonly NewsService _newsService;
    private readonly ILogger<NewsPage> _logger;
    private readonly ObservableCollection<NewsListEntry> _entries = new();

    public NewsPage(NewsService newsService, ILogger<NewsPage> logger)
    {
        InitializeComponent();
        _newsService = newsService;
        _logger = logger;
        NewsList.ItemsSource = _entries;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _newsService.PopupNewsAvailable += OnPopupNewsAvailable;
        RefreshEntries();
        TxtSyncStatus.Text = _entries.Count == 0 ? "News will appear here when available." : "Showing locally cached news.";
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => _newsService.PopupNewsAvailable -= OnPopupNewsAvailable;

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        TxtSyncStatus.Text = "Checking for news...";
        try
        {
            NewsSyncResult result = await _newsService.SynchronizeAsync();
            RefreshEntries();
            TxtSyncStatus.Text = result.Succeeded
                ? $"News checked. {_entries.Count} item{(_entries.Count == 1 ? string.Empty : "s")} available."
                : "Could not reach the news service. Showing saved news.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual PocketMC news sync failed.");
            TxtSyncStatus.Text = "Could not reach the news service. Showing saved news.";
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private void NewsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NewsList.SelectedItem is not NewsListEntry entry)
        {
            ShowEmptyState("Choose a news item to read.");
            return;
        }

        ArticleContent.Children.Clear();
        ArticleContent.Children.Add(NewsContentRenderer.Build(entry.Item));
        TxtArticleMeta.Text = $"{entry.TypeAndPriority}  ·  {entry.Item.Metadata.PublishedUtc.ToLocalTime():g}";
        NewsArticleSurface.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        _newsService.Acknowledge(entry.Item.Metadata.Id);
        entry.MarkRead();
    }

    private void RefreshEntries(string? selectedId = null)
    {
        string? previousId = selectedId ?? (NewsList.SelectedItem as NewsListEntry)?.Item.Metadata.Id;
        var readIds = _newsService.GetAcknowledgedNewsIds();
        var items = _newsService.GetCachedNews()
            .OrderByDescending(item => item.Metadata.PublishedUtc)
            .ThenBy(item => item.Metadata.Id, StringComparer.Ordinal)
            .ToArray();

        _entries.Clear();
        foreach (NewsItem item in items)
        {
            _entries.Add(new NewsListEntry(item, readIds.Contains(item.Metadata.Id)));
        }

        NewsList.SelectedItem = previousId == null
            ? null
            : _entries.FirstOrDefault(entry => entry.Item.Metadata.Id == previousId);

        if (_entries.Count == 0)
        {
            NewsArticleSurface.Visibility = Visibility.Collapsed;
            ShowEmptyState("No news is available yet. PocketMC will check again later.");
        }
        else if (NewsList.SelectedItem == null)
        {
            ShowEmptyState("Choose a news item to read.");
        }
    }

    private void ShowEmptyState(string message)
    {
        TxtEmpty.Text = message;
        EmptyPanel.Visibility = Visibility.Visible;
        NewsArticleSurface.Visibility = Visibility.Collapsed;
    }

    private void OnPopupNewsAvailable(System.Collections.Generic.IReadOnlyList<NewsItem> items)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsLoaded) RefreshEntries();
        }));
    }

    public sealed class NewsListEntry : INotifyPropertyChanged
    {
        public NewsItem Item { get; }
        public string Title { get; }
        public string TypeAndPriority { get; }
        public string PublishedLabel { get; }
        public Visibility UnreadVisibility { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public NewsListEntry(NewsItem item, bool isRead)
        {
            Item = item;
            Title = item.Blocks.FirstOrDefault(block => block.Type == NewsBlockType.Title)?.Text ?? item.Metadata.Id;
            TypeAndPriority = $"{Format(item.Metadata.Type)}  ·  {Format(item.Metadata.Priority)}";
            PublishedLabel = item.Metadata.PublishedUtc.ToLocalTime().ToString("d");
            UnreadVisibility = isRead ? Visibility.Collapsed : Visibility.Visible;
        }

        public void MarkRead()
        {
            if (UnreadVisibility == Visibility.Collapsed) return;
            UnreadVisibility = Visibility.Collapsed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnreadVisibility)));
        }

        private static string Format<T>(T value) where T : Enum
            => System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<!^)([A-Z])", " $1");
    }
}