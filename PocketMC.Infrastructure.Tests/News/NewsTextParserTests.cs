using PocketMC.Infrastructure.News;

namespace PocketMC.Infrastructure.Tests.News;

public sealed class NewsTextParserTests
{
    private readonly NewsTextParser _parser = new();

    [Fact]
    public void Parse_ReadsMetadataAndStructuredPlainText()
    {
        NewsItem item = _parser.Parse("announcement.txt", ValidNews);

        Assert.Equal("playit-agent-fix", item.Metadata.Id);
        Assert.Equal(NewsType.QuickFix, item.Metadata.Type);
        Assert.Equal(NewsPriority.Critical, item.Metadata.Priority);
        Assert.True(item.Metadata.Popup);
        Assert.Equal(new Version(1, 9, 9), item.Metadata.MinimumVersion);
        Assert.Equal(NewsBlockType.Title, item.Blocks[0].Type);
        Assert.Equal(NewsBlockType.BulletList, item.Blocks[3].Type);
        Assert.Equal(NewsBlockType.Warning, item.Blocks[4].Type);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    public void Parse_RejectsUnsafeLinkSchemes(string url)
    {
        string source = ValidNews.Replace("https://pocketmc.example/help", url, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => _parser.Parse("news.txt", source));
    }

    [Fact]
    public void Parse_RejectsHtmlAndUnknownMetadata()
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse("news.txt", ValidNews.Replace("Some users may see the agent stuck at Ready.", "<script>alert(1)</script>", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => _parser.Parse("news.txt", ValidNews.Replace("priority: critical", "color: red", StringComparison.Ordinal)));
    }

    [Fact]
    public void Parse_OnlyAllowsReadMarkerInLocalCachedNews()
    {
        string locallyRead = ValidNews.Replace("maxVersion: 1.9.9", "maxVersion: 1.9.9\nread: true", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => _parser.Parse("news.txt", locallyRead));
        Assert.True(_parser.ParseCached("news.txt", locallyRead).Metadata.IsRead);
        Assert.Contains("read: true", _parser.MarkRead("news.txt", ValidNews), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsDuplicateIdsInContentAndInvalidTargetRange()
    {
        string duplicateTitle = ValidNews.Replace("heading: What happened", "title: Duplicate\n\nheading: What happened", StringComparison.Ordinal);
        string invalidRange = ValidNews.Replace("maxVersion: 1.9.9", "maxVersion: 1.0.0", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => _parser.Parse("news.txt", duplicateTitle));
        Assert.Throws<InvalidDataException>(() => _parser.Parse("news.txt", invalidRange));
    }

    [Fact]
    public void Parse_RepositoryPlayitNoticeIsValidAndTargeted()
    {
        string newsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "news", "2026-09-29-playit-agent-fix.txt"));
        NewsItem item = _parser.Parse(Path.GetFileName(newsPath), File.ReadAllText(newsPath));

        Assert.Equal("playit-agent-stuck-ready-2026-09-29", item.Metadata.Id);
        Assert.Equal(NewsType.QuickFix, item.Metadata.Type);
        Assert.Equal(NewsPriority.Critical, item.Metadata.Priority);
        Assert.True(item.Metadata.Popup);
        Assert.Equal(new Version(1, 9, 9), item.Metadata.MinimumVersion);
        Assert.Equal(new Version(1, 9, 9, 1), item.Metadata.MaximumVersion);
    }

    [Fact]
    public void Parse_AllRepositoryNewsFiles()
    {
        string newsDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "news"));
        string[] files = Directory.GetFiles(newsDirectory, "*.txt", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            NewsItem item = _parser.Parse(Path.GetFileName(file), File.ReadAllText(file));
            Assert.False(string.IsNullOrWhiteSpace(item.Metadata.Id));
            Assert.Contains(item.Blocks, block => block.Type == NewsBlockType.Title);
        }
    }

    [Fact]
    public void Parse_FormatShowcaseExercisesEverySupportedContentElement()
    {
        NewsItem item = _parser.Parse("format-showcase.txt", FormatShowcaseNews);
        HashSet<NewsBlockType> parsedTypes = item.Blocks.Select(block => block.Type).ToHashSet();

        Assert.Equal(Enum.GetValues<NewsBlockType>().ToHashSet(), parsedTypes);
        Assert.False(item.Metadata.Popup);
        Assert.Equal(new Version(1, 9, 9, 1), item.Metadata.MinimumVersion);
        Assert.Equal(item.Metadata.MinimumVersion, item.Metadata.MaximumVersion);
    }

    private const string ValidNews = """
        ---
        id: playit-agent-fix
        type: quick-fix
        priority: critical
        popup: true
        published: 2026-09-29T18:00:00Z
        expires:
        minVersion: 1.9.9
        maxVersion: 1.9.9
        ---
        title: Playit Connection Issue

        heading: What happened

        paragraph:
        Some users may see the agent stuck at Ready.

        list:
        - Open the Tunnel page
        - Download the agent again

        warning:
        Keep your saved credentials.

        link: Help | https://pocketmc.example/help
        """;

    private const string FormatShowcaseNews = """
        ---
        id: format-showcase-test
        type: announcement
        priority: important
        popup: false
        published: 2026-09-30T00:00:00Z
        expires: 2026-10-07T00:00:00Z
        minVersion: 1.9.9.1
        maxVersion: 1.9.9.1
        ---
        title: News Format Showcase

        subtitle: Supported content sample

        heading: Plain text

        paragraph:
        This sample exercises every supported News content element.

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
        Popup behavior is controlled separately by metadata.

        heading: Displayed code

        code:
        echo "News code blocks are display-only"

        link: PocketMC website | https://pocketmc.github.io/

        divider:
        """;
}