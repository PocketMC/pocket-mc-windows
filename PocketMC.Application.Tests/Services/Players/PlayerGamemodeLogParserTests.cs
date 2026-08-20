using PocketMC.Application.Services.Players;
using Xunit;

namespace PocketMC.Application.Tests.Services.Players;

public sealed class PlayerGamemodeLogParserTests
{
    [Theory]
    [InlineData("[12:34:56 INFO]: Set Steve's game mode to Creative Mode", "Steve", "creative")]
    [InlineData("[12:34:56] [Server thread/INFO]: Set Alex's game mode to Survival Mode", "Alex", "survival")]
    [InlineData("Set Notch's game mode to Spectator Mode", "Notch", "spectator")]
    [InlineData("Set Jeb's game mode to Adventure Mode", "Jeb", "adventure")]
    [InlineData("[Server: Set Steve's game mode to Creative Mode]", "Steve", "creative")]
    [InlineData("[Admin: Set Player One's game mode to Survival Mode]", "Player One", "survival")]
    public void TryParse_JavaSetTargetGamemode_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("[Steve: Set own game mode to Creative Mode]", "Steve", "creative")]
    [InlineData("[Alex: Set own game mode to Survival Mode]", "Alex", "survival")]
    [InlineData("[12:34:56 INFO]: [Player123: Set own game mode to Spectator Mode]", "Player123", "spectator")]
    [InlineData("Steve set own game mode to Creative Mode", "Steve", "creative")]
    [InlineData("Alex set own game mode to Adventure", "Alex", "adventure")]
    public void TryParse_JavaSetOwnGamemode_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("Steve's game mode has been updated to Creative Mode", "Steve", "creative")]
    [InlineData("Player Alex's game mode has been updated to Survival", "Alex", "survival")]
    [InlineData("[12:34:56 INFO]: Steve's game mode was changed to Creative Mode", "Steve", "creative")]
    public void TryParse_GamemodeUpdated_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("Game mode of Steve has been updated to Creative Mode", "Steve", "creative")]
    [InlineData("Game mode of player 'Master Chief 117' changed to 'creative'", "Master Chief 117", "creative")]
    [InlineData("[2026-04-28 18:10:30:571 INFO] Game mode of SahajItaliya has been updated to Survival Mode", "SahajItaliya", "survival")]
    [InlineData("Game mode of player \"Bedrock Gamer\" was changed to adventure", "Bedrock Gamer", "adventure")]
    [InlineData("Player Steve's game mode has been updated to Creative", "Steve", "creative")]
    [InlineData("Player SahajItaliya set game mode to Creative", "SahajItaliya", "creative")]
    public void TryParse_BedrockVariants_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("[Essentials] Set game mode Creative for Steve", "Steve", "creative")]
    [InlineData("Set game mode survival for Alex", "Alex", "survival")]
    [InlineData("Gamemode for Steve set to spectator", "Steve", "spectator")]
    [InlineData("Game mode for Player_99 set to adventure", "Player_99", "adventure")]
    public void TryParse_PluginVariants_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("Steve issued server command: /gamemode adventure", "Steve", "adventure")]
    [InlineData("Steve issued server command: /gamemode creative Friend", "Friend", "creative")]
    [InlineData("Steve issued server command: /gamemode 2", "Steve", "adventure")]
    [InlineData("Steve issued server command: /gamemode 1 Alex", "Alex", "creative")]
    [InlineData("Steve issued server command: /gm creative", "Steve", "creative")]
    [InlineData("Steve issued server command: /gma", "Steve", "adventure")]
    [InlineData("Steve issued server command: /gmc Notch", "Notch", "creative")]
    [InlineData("Player Steve executed command: /gamemode 2", "Steve", "adventure")]
    [InlineData("Player Steve executed command: /gamemode adventure Friend", "Friend", "adventure")]
    public void TryParse_IssuedServerCommands_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("Steve has the following entity data: 2", "Steve", "adventure")]
    [InlineData("Alex has the following entity data: 1b", "Alex", "creative")]
    [InlineData("Friend has the following entity data: 0", "Friend", "survival")]
    [InlineData("Notch has the following entity data: 3d", "Notch", "spectator")]
    [InlineData("[Server: Steve has the following entity data: 2]", "Steve", "adventure")]
    public void TryParse_EntityDataResponses_ParsesCorrectly(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("\x1B[33mSet Steve's game mode to Creative Mode\x1B[0m", "Steve", "creative")]
    [InlineData("Command output | Set Alex's game mode to Survival Mode", "Alex", "survival")]
    public void TryParse_AnsiAndCommandPrefixes_CleansAndParses(string logLine, string expectedPlayer, string expectedMode)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);

        Assert.NotNull(result);
        Assert.Equal(expectedPlayer, result!.PlayerName);
        Assert.Equal(expectedMode, result.Gamemode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Player connected: Steve, xuid: 2535452924809484")]
    [InlineData("There are 2 of a max of 20 players online: Steve, Alex")]
    [InlineData("Saving the game...")]
    [InlineData("Thread [Server thread] is running smoothly")]
    public void TryParse_IrrelevantLines_ReturnsNullFast(string logLine)
    {
        PlayerGamemodeChangeEvent? result = PlayerGamemodeLogParser.TryParse(logLine);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("survival", "survival")]
    [InlineData("SURVIVAL", "survival")]
    [InlineData("0", "survival")]
    [InlineData("s", "survival")]
    [InlineData("creative", "creative")]
    [InlineData("1", "creative")]
    [InlineData("c", "creative")]
    [InlineData("adventure", "adventure")]
    [InlineData("2", "adventure")]
    [InlineData("a", "adventure")]
    [InlineData("spectator", "spectator")]
    [InlineData("3", "spectator")]
    [InlineData("sp", "spectator")]
    [InlineData("unknown_mode", null)]
    public void NormalizeGamemode_HandlesAllSynonyms(string input, string? expected)
    {
        Assert.Equal(expected, PlayerGamemodeLogParser.NormalizeGamemode(input));
    }
}
