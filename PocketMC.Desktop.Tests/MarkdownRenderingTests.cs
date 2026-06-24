using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Tests;

public sealed class MarkdownRenderingTests
{
    [Fact]
    public void NativeMarkdownRenderer_RendersEmojiShortcodes()
    {
        RunOnSta(() =>
        {
            FlowDocument document = MarkdownFlowDocumentConverter.Convert(
                "## :warning: Watch this\n\nDone :white_check_mark: and happy :smile:",
                isDarkMode: true);

            string text = string.Concat(EnumerateInlineText(document));

            Assert.Contains("\u26A0", text);
            Assert.Contains("\u2705", text);
            Assert.Contains("\U0001F604", text);
            Assert.DoesNotContain(":warning:", text);
            Assert.DoesNotContain(":white_check_mark:", text);
            Assert.DoesNotContain(":smile:", text);
        });
    }

    [Fact]
    public void NativeMarkdownRenderer_UsesEmojiWpfForUnicodeEmojiGlyphs()
    {
        RunOnSta(() =>
        {
            FlowDocument document = MarkdownFlowDocumentConverter.Convert(
                "## \U0001F511 Key Events\n\n\u26A0\uFE0F Watch lag spikes and \U0001F604 player moments.",
                isDarkMode: true);

            Emoji.Wpf.TextBlock[] emojiBlocks = EnumerateEmojiTextBlocks(document).ToArray();

            Assert.Contains(emojiBlocks, block => block.Text == "\U0001F511");
            Assert.Contains(emojiBlocks, block => block.Text.Contains("\u26A0"));
            Assert.Contains(emojiBlocks, block => block.Text == "\U0001F604");
        });
    }

    [Fact]
    public void NativeMarkdownRenderer_UsesEmojiWpfInsideBoldHeadings()
    {
        RunOnSta(() =>
        {
            FlowDocument document = MarkdownFlowDocumentConverter.Convert(
                "## **\U0001F4CC Key Events**\n\n## **\u26A0\uFE0F Warnings & Issues**",
                isDarkMode: true);

            Emoji.Wpf.TextBlock[] emojiBlocks = EnumerateEmojiTextBlocks(document).ToArray();

            Assert.NotEmpty(emojiBlocks);
            Assert.All(emojiBlocks, block => Assert.IsType<Emoji.Wpf.TextBlock>(block));
        });
    }

    [Fact]
    public void NativeMarkdownRenderer_UsesInlineTextBlocksForEmojiGlyphs()
    {
        RunOnSta(() =>
        {
            FlowDocument document = MarkdownFlowDocumentConverter.Convert(
                "## **\U0001F4CC Key Events**\n\nDone \u2705 and happy \U0001F604",
                isDarkMode: true);

            Emoji.Wpf.TextBlock[] emojiBlocks = EnumerateEmojiTextBlocks(document).ToArray();

            Assert.Contains(emojiBlocks, block => block.Text == "\U0001F4CC");
            Assert.Contains(emojiBlocks, block => block.Text == "\u2705");
            Assert.Contains(emojiBlocks, block => block.Text == "\U0001F604");
            Assert.All(emojiBlocks, block => Assert.IsType<Emoji.Wpf.TextBlock>(block));
        });
    }

    [Fact]
    public void NativeMarkdownRenderer_UsesEmojiWpfTextBlocksForEmojiGlyphs()
    {
        RunOnSta(() =>
        {
            FlowDocument document = MarkdownFlowDocumentConverter.Convert(
                "## **\U0001F4CC Key Events**\n\nDone \u2705",
                isDarkMode: true);

            Emoji.Wpf.TextBlock[] emojiBlocks = EnumerateEmojiTextBlocks(document).ToArray();

            Assert.NotEmpty(emojiBlocks);
            Assert.All(emojiBlocks, block => Assert.IsType<Emoji.Wpf.TextBlock>(block));
        });
    }

    [Fact]
    public void MarkdownSummaryUi_DoesNotReferenceWebView2()
    {
        string projectFile = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "PocketMC.Desktop.csproj"));
        string intelligenceDir = Path.GetDirectoryName(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Intelligence",
            "NativeMarkdownViewer.xaml.cs"))!;

        string[] rendererSources = Directory.GetFiles(intelligenceDir, "*.cs")
            .Concat(Directory.GetFiles(intelligenceDir, "*.xaml"))
            .ToArray();
        string rendererSourceText = string.Join(Environment.NewLine, rendererSources.Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.Web.WebView2", projectFile);
        Assert.DoesNotContain("using Microsoft.Web.WebView2", rendererSourceText);
        Assert.DoesNotContain("new WebView2", rendererSourceText);
        Assert.DoesNotContain("EnsureCoreWebView2Async", rendererSourceText);
    }

    private static IEnumerable<Run> EnumerateRuns(FlowDocument document)
    {
        return document.Blocks.SelectMany(EnumerateRuns);
    }

    private static IEnumerable<Run> EnumerateRuns(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (Run run in EnumerateRuns(paragraph.Inlines))
                    yield return run;
                break;
            case List list:
                foreach (ListItem item in list.ListItems)
                    foreach (Block child in item.Blocks)
                        foreach (Run run in EnumerateRuns(child))
                            yield return run;
                break;
            case Section section:
                foreach (Block child in section.Blocks)
                    foreach (Run run in EnumerateRuns(child))
                        yield return run;
                break;
            case Table table:
                foreach (TableRowGroup group in table.RowGroups)
                    foreach (TableRow row in group.Rows)
                        foreach (TableCell cell in row.Cells)
                            foreach (Block child in cell.Blocks)
                                foreach (Run run in EnumerateRuns(child))
                                    yield return run;
                break;
        }
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return run;
                    break;
                case Span span:
                    foreach (Run child in EnumerateRuns(span.Inlines))
                        yield return child;
                    break;
            }
        }
    }

    private static IEnumerable<Emoji.Wpf.TextBlock> EnumerateEmojiTextBlocks(FlowDocument document)
    {
        return document.Blocks.SelectMany(EnumerateEmojiTextBlocks);
    }

    private static IEnumerable<string> EnumerateInlineText(FlowDocument document)
    {
        return document.Blocks.SelectMany(EnumerateInlineText);
    }

    private static IEnumerable<Emoji.Wpf.TextBlock> EnumerateEmojiTextBlocks(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (Emoji.Wpf.TextBlock textBlock in EnumerateEmojiTextBlocks(paragraph.Inlines))
                    yield return textBlock;
                break;
            case List list:
                foreach (ListItem item in list.ListItems)
                    foreach (Block child in item.Blocks)
                        foreach (Emoji.Wpf.TextBlock textBlock in EnumerateEmojiTextBlocks(child))
                            yield return textBlock;
                break;
            case Section section:
                foreach (Block child in section.Blocks)
                    foreach (Emoji.Wpf.TextBlock textBlock in EnumerateEmojiTextBlocks(child))
                        yield return textBlock;
                break;
            case Table table:
                foreach (TableRowGroup group in table.RowGroups)
                    foreach (TableRow row in group.Rows)
                        foreach (TableCell cell in row.Cells)
                            foreach (Block child in cell.Blocks)
                                foreach (Emoji.Wpf.TextBlock textBlock in EnumerateEmojiTextBlocks(child))
                                    yield return textBlock;
                break;
        }
    }

    private static IEnumerable<Emoji.Wpf.TextBlock> EnumerateEmojiTextBlocks(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: Emoji.Wpf.TextBlock textBlock }:
                    yield return textBlock;
                    break;
                case Span span:
                    foreach (Emoji.Wpf.TextBlock child in EnumerateEmojiTextBlocks(span.Inlines))
                        yield return child;
                    break;
            }
        }
    }

    private static IEnumerable<string> EnumerateInlineText(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (string text in EnumerateInlineText(paragraph.Inlines))
                    yield return text;
                break;
            case List list:
                foreach (ListItem item in list.ListItems)
                    foreach (Block child in item.Blocks)
                        foreach (string text in EnumerateInlineText(child))
                            yield return text;
                break;
            case Section section:
                foreach (Block child in section.Blocks)
                    foreach (string text in EnumerateInlineText(child))
                        yield return text;
                break;
            case Table table:
                foreach (TableRowGroup group in table.RowGroups)
                    foreach (TableRow row in group.Rows)
                        foreach (TableCell cell in row.Cells)
                            foreach (Block child in cell.Blocks)
                                foreach (string text in EnumerateInlineText(child))
                                    yield return text;
                break;
        }
    }

    private static IEnumerable<string> EnumerateInlineText(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return run.Text;
                    break;
                case InlineUIContainer { Child: Emoji.Wpf.TextBlock textBlock }:
                    yield return textBlock.Text;
                    break;
                case InlineUIContainer { Child: TextBlock textBlock }:
                    yield return textBlock.Text;
                    break;
                case Span span:
                    foreach (string child in EnumerateInlineText(span.Inlines))
                        yield return child;
                    break;
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
            throw exception;
    }
}

