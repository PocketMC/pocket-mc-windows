using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Features.News;
using PocketMC.Desktop.Tests.TestSupport.Utilities;
using PocketMC.Infrastructure.News;

namespace PocketMC.Desktop.Tests.Features.News;

public sealed class NewsArticleViewTests
{
    [Fact]
    public void NewsPageAndPopup_UseSameArticleViewComponent()
    {
        string newsPageXaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop", "Features", "News", "NewsPage.xaml"));
        string popupXaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop", "Features", "News", "NewsPopupWindow.xaml"));

        Assert.Contains("<news:NewsArticleView x:Name=\"ArticleView\"", newsPageXaml, StringComparison.Ordinal);
        Assert.Contains("<news:NewsArticleView x:Name=\"ArticleView\"", popupXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_CreatesSharedBadgesAndReservedScrollbarGutter()
    {
        NewsArticleView? view = null;
        RunInSta(() =>
        {
            NewsMetadata metadata = new(
                "shared-view-test",
                NewsType.Announcement,
                NewsPriority.Important,
                Popup: false,
                PublishedUtc: new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                ExpiresUtc: null,
                MinimumVersion: null,
                MaximumVersion: null);
            NewsItem item = new(
                "shared-view-test.txt",
                string.Empty,
                metadata,
                new[] { new NewsContentBlock(NewsBlockType.Title, "Shared article view") });

            view = new NewsArticleView();
            view.Display(item);

            Grid root = Assert.IsType<Grid>(view.Content);
            WrapPanel badges = Assert.Single(root.Children.OfType<WrapPanel>());
            Assert.Equal(3, badges.Children.Count);

            ScrollViewer scrollViewer = Assert.Single(root.Children.OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
            Assert.Equal(28, scrollViewer.Padding.Right);

            StackPanel content = Assert.IsType<StackPanel>(scrollViewer.Content);
            Assert.Single(content.Children);
        });

        Assert.NotNull(view);
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}