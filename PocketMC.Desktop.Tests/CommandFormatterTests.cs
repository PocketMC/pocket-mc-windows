using PocketMC.Application.Services.Players;

namespace PocketMC.Desktop.Tests;

public sealed class CommandFormatterTests
{
    [Fact]
    public void FormatPlayerName_LeavesPlainJavaNamesUnquoted()
    {
        string formatted = CommandFormatter.FormatPlayerName("Sahaj33", "Paper");

        Assert.Equal("Sahaj33", formatted);
    }

    [Fact]
    public void FormatPlayerName_RejectsJavaNamesWithSpecialCharacters()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CommandFormatter.FormatPlayerName(".SahajItaliya", "Fabric"));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("Steve;stop")]
    [InlineData("Steve Jobs")]
    [InlineData("Steve$")]
    [InlineData("toolongname1234567890")]
    public void IsValidPlayerName_RejectsUnsafeJavaNames(string name)
    {
        Assert.False(CommandFormatter.IsValidPlayerName(name, "Paper"));
    }

    [Fact]
    public void FormatPlayerName_QuotesPocketMineNamesWithSpaces()
    {
        string formatted = CommandFormatter.FormatPlayerName("Sahaj Italiya", "Pocketmine (PHP)");

        Assert.Equal("\"Sahaj Italiya\"", formatted);
    }

    [Fact]
    public void FormatPlayerName_QuotesPocketMinePlainNames()
    {
        string formatted = CommandFormatter.FormatPlayerName("Sahaj", "Pocketmine (PHP)");

        Assert.Equal("\"Sahaj\"", formatted);
    }

    [Theory]
    [InlineData("Sahaj \"The Builder\"")]
    [InlineData("Sahaj\\Builder")]
    [InlineData("Sahaj\nBuilder")]
    [InlineData("Sahaj'Builder")]
    public void IsValidPlayerName_RejectsUnsafeBedrockNames(string name)
    {
        Assert.False(CommandFormatter.IsValidPlayerName(name, "Bedrock (BDS)"));
    }

    [Fact]
    public void TryFormatPlayerName_QuotesValidBedrockNamesWithSpaces()
    {
        bool success = CommandFormatter.TryFormatPlayerName("Sahaj Italiya", "Bedrock (BDS)", out string formatted);

        Assert.True(success);
        Assert.Equal("\"Sahaj Italiya\"", formatted);
    }
}


