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
    public void ParseLine_ParsesBedrockMultilineHeaderAndPlainNameContinuation()
    {
        PlayerListParseResult? header = _parser.ParseLine(
            "[2026-04-28 18:10:38:971 INFO] There are 1/10 players online:",
            "Bedrock (BDS)");

        Assert.NotNull(header);
        Assert.Equal(1, header!.OnlinePlayerCount);
        Assert.Equal(10, header.MaxPlayers);
        Assert.False(header.IsComplete);
        Assert.Equal(PlayerListContinuationStyle.BedrockPlainNames, header.ContinuationStyle);

        Assert.True(_parser.TryParseContinuationLine("SahajItaliya", header.ContinuationStyle, out string name));
        Assert.Equal("SahajItaliya", name);
    }

    [Fact]
    public void ParseLine_ParsesBedrockZeroPlayerHeaderAsComplete()
    {
        PlayerListParseResult? result = _parser.ParseLine(
            "[2026-04-28 18:10:38:971 INFO] There are 0/10 players online:",
            "Bedrock (BDS)");

        Assert.NotNull(result);
        Assert.Equal(0, result!.OnlinePlayerCount);
        Assert.True(result.IsComplete);
        Assert.Empty(result.OnlinePlayerNames);
    }

    [Fact]
    public void TryParseContinuationLine_StopsBedrockPlainNamesAtTimestampedLogLine()
    {
        Assert.False(_parser.TryParseContinuationLine(
            "[2026-04-28 18:10:49:015 INFO] There are 1/10 players online:",
            PlayerListContinuationStyle.BedrockPlainNames,
            out _));
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
    public void ParseLine_StripsPocketMineCommandOutputPrefixFromPlayerName()
    {
        PlayerListParseResult? result = _parser.ParseLine(
            "Online players (1/20): Command output | SahajItaliya",
            "Pocketmine (PHP)");

        Assert.NotNull(result);
        Assert.Equal(1, result!.OnlinePlayerCount);
        Assert.Equal(new[] { "SahajItaliya" }, result.OnlinePlayerNames);
    }

    [Theory]
    [InlineData("Command output | SahajItaliya", "SahajItaliya")]
    [InlineData("Command output   |   SahajItaliya", "SahajItaliya")]
    [InlineData("Command output | Command output | SahajItaliya", "SahajItaliya")]
    [InlineData("SahajItaliya", "SahajItaliya")]
    public void NormalizePlayerName_StripsPocketMineCommandOutputWrapper(string rawName, string expected)
    {
        Assert.Equal(expected, PlayerListParser.NormalizePlayerName(rawName));
    }

    [Fact]
    public void TryParseContinuationLine_StripsCommandOutputWrapperFromNames()
    {
        Assert.True(_parser.TryParseContinuationLine(
            "Command output | SahajItaliya",
            PlayerListContinuationStyle.BedrockPlainNames,
            out string name));

        Assert.Equal("SahajItaliya", name);
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

    [Fact]
    public void ParseLine_SplitsCommaSeparatedNamesWithoutSpaces()
    {
        PlayerListParseResult? result = _parser.ParseLine(
            "There are 2 of a max of 20 players online: Steve,Alex",
            "Paper");

        Assert.NotNull(result);
        Assert.Equal(new[] { "Steve", "Alex" }, result!.OnlinePlayerNames);
    }

    [Fact]
    public void TryParseContinuationLine_RejectsOrdinaryHyphenatedMessages()
    {
        Assert.False(_parser.TryParseContinuationLine("Steve - not a list item", out _));
    }
}


