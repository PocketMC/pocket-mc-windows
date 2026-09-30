using System;
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
            NewsItem item = new NewsTextParser().Parse("format-showcase.txt", FormatShowcaseNews);

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

            Border codeBlock = Assert.IsType<Border>(rendered.Children[11]);
            TextBox codeText = Assert.IsType<TextBox>(codeBlock.Child);
            Assert.True(codeText.IsReadOnly);
            Assert.Equal(TextWrapping.Wrap, codeText.TextWrapping);
            Assert.Equal(ScrollBarVisibility.Disabled, codeText.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, codeText.VerticalScrollBarVisibility);
            Assert.Equal("Consolas", codeText.FontFamily.Source);
        });
    }

    private const string FormatShowcaseNews = """
        ---
        id: renderer-format-showcase-test
        type: announcement
        priority: important
        popup: false
        published: 2026-09-30T00:00:00Z
        ---
        title: News Format Showcase

        subtitle: Supported content sample

        heading: Plain text

        paragraph:
        This sample exercises supported News content elements.

        heading: Bullet list

        list:
        - First bullet item
        - Second bullet item

        heading: Numbered list

        numbered-list:
        1. First ordered step
        2. Second ordered step

        warning:
        This warning is display-only.

        important:
        Popup behavior is configured separately in metadata.

        heading: Displayed code

        code:
        echo "News code blocks are display-only"

        link: PocketMC website | https://pocketmc.github.io/

        divider:
        """;

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