using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows;
using PocketMC.Desktop.Features.News;
using PocketMC.Infrastructure.News;

namespace PocketMC.Desktop.Tests.Features.News;

public sealed class NewsContentRendererTests
{
    [Fact]
    public void Build_ShowcaseRendersEverySupportedElementAsNativeWpfControls()
    {
        RunInSta(() =>
        {
            string newsPath = Path.Combine(AppContext.BaseDirectory, "NewsFixtures", "2026-09-30-format-showcase.txt");
            NewsItem item = new NewsTextParser().Parse(Path.GetFileName(newsPath), File.ReadAllText(newsPath));

            StackPanel rendered = NewsContentRenderer.Build(item);

            Assert.Equal(item.Blocks.Count, rendered.Children.Count);
            Assert.Collection(
                rendered.Children.Cast<UIElement>(),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<StackPanel>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<StackPanel>(element),
                element => Assert.IsType<Border>(element),
                element => Assert.IsType<Border>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<Border>(element),
                element => Assert.IsType<TextBlock>(element),
                element => Assert.IsType<Separator>(element));

            TextBlock linkText = Assert.IsType<TextBlock>(rendered.Children[12]);
            Hyperlink link = Assert.IsType<Hyperlink>(Assert.Single(linkText.Inlines));
            Assert.Equal(Uri.UriSchemeHttps, link.NavigateUri!.Scheme);
        });
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