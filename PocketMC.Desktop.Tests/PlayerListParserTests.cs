using PocketMC.Desktop.Features.Players.Services;

namespace PocketMC.Desktop.Tests;

public sealed class PlayerListParserTests
{
    private readonly PlayerListParser _parser = new();

    [Fact]
    public void ParseLine_ParsesJavaInlineList()
    {
        PlayerListParseResult? result = _parser.ParseLine(
            "There are 2 of a max of 20 players online: Steve, Alex",
            "Paper");

        Assert.NotNull(result);
        Assert.Equal(2, result!.OnlinePlayerCount);
        Assert.Equal(20, result.MaxPlayers);
        Assert.True(result.IsComplete);
        Assert.Equal(new[] { "Steve", "Alex" }, result.OnlinePlayerNames);
    }

    [Fact]
    public void ParseLine_ParsesBedrockNamesWithSpaces()
    {
        PlayerListParseResult? result = _parser.ParseLine(
            "Players connected (2/20): Steve, Alex With Spaces",
            "Bedrock");

        Assert.NotNull(result);
        Assert.Equal(new[] { "Steve", "Alex With Spaces" }, result!.OnlinePlayerNames);
    }

    [Fact]
    public void ParseLine_ParsesPocketMineList()
    {
        PlayerListParseResult? result = _parser.ParseLine(
            "Online players (1/20): Alex With Spaces",
            "Pocketmine-MP");

        Assert.NotNull(result);
        Assert.Equal(1, result!.OnlinePlayerCount);
        Assert.Equal(new[] { "Alex With Spaces" }, result.OnlinePlayerNames);
    }

    [Fact]
    public void ParseLine_ParsesJavaMultilineHeaderAndContinuation()
    {
        PlayerListParseResult? header = _parser.ParseLine(
            "There are 2 players online:",
            "Spigot");

        Assert.NotNull(header);
        Assert.False(header!.IsComplete);
        Assert.Equal(2, header.OnlinePlayerCount);

        Assert.True(_parser.TryParseContinuationLine("- Steve", out string first));
        Assert.True(_parser.TryParseContinuationLine("- Alex With Spaces", out string second));
        Assert.Equal("Steve", first);
        Assert.Equal("Alex With Spaces", second);
    }
}
