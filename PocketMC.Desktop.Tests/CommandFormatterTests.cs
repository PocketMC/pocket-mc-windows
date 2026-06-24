using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Views.Behaviors;
using PocketMC.Desktop.Infrastructure;

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
    public void FormatPlayerName_DoesNotQuoteJavaCrossplayNamesWithSpecialCharacters()
    {
        string formatted = CommandFormatter.FormatPlayerName(".SahajItaliya", "Fabric");

        Assert.Equal(".SahajItaliya", formatted);
    }

    [Fact]
    public void FormatPlayerName_QuotesPocketMineNamesWithSpaces()
    {
        string formatted = CommandFormatter.FormatPlayerName("Sahaj Italiya", "Pocketmine (PHP)");

        Assert.Equal("\"Sahaj Italiya\"", formatted);
    }

    [Fact]
    public void FormatPlayerName_EscapesQuotedNames()
    {
        string formatted = CommandFormatter.FormatPlayerName("Sahaj \"The Builder\"", "Bedrock (BDS)");

        Assert.Equal("\"Sahaj \\\"The Builder\\\"\"", formatted);
    }
}


