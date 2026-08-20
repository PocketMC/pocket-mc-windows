using PocketMC.Infrastructure.WhatsNew;
using Xunit;

namespace PocketMC.Infrastructure.Tests.WhatsNew;

public sealed class ChangelogParserTests
{
    [Fact]
    public void Parse_ValidChangelogWithDashesAndPlainLines_StripsDashesAndParsesSections()
    {
        string raw = @"VERSION=1.9.7

[NEW FEATURES]
- First feature with dash
* Second feature with asterisk
• Third feature with bullet
Fourth feature without prefix

[FIXES]
- Fixed issue A
Fixed issue B
";

        ChangelogEntry? result = ChangelogParser.Parse(raw);

        Assert.NotNull(result);
        Assert.Equal("1.9.7", result!.Version);
        Assert.Equal(2, result.Sections.Count);

        var features = result.Sections[0];
        Assert.Equal("NEW FEATURES", features.Name);
        Assert.Equal(4, features.Items.Count);
        Assert.Equal("First feature with dash", features.Items[0]);
        Assert.Equal("Second feature with asterisk", features.Items[1]);
        Assert.Equal("Third feature with bullet", features.Items[2]);
        Assert.Equal("Fourth feature without prefix", features.Items[3]);

        var fixes = result.Sections[1];
        Assert.Equal("FIXES", fixes.Name);
        Assert.Equal(2, fixes.Items.Count);
        Assert.Equal("Fixed issue A", fixes.Items[0]);
        Assert.Equal("Fixed issue B", fixes.Items[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Some random text without version")]
    public void Parse_InvalidInput_ReturnsNull(string? input)
    {
        ChangelogEntry? result = ChangelogParser.Parse(input);
        Assert.Null(result);
    }
}
