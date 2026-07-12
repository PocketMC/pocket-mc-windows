using PocketMC.Application.Services.Players;

namespace PocketMC.Desktop.Tests;

public sealed class PlayerActionCommandBuilderTests
{
    [Fact]
    public void BuildSubmitCommands_UsesKickForBedrockBanBecauseSidecarIsAuthoritative()
    {
        IReadOnlyList<string> commands = PlayerActionCommandBuilder.BuildSubmitCommands(
            "Ban",
            "\"Sahaj Italiya\"",
            "Bedrock (BDS)",
            "griefing");

        Assert.Equal(new[] { "kick \"Sahaj Italiya\" griefing" }, commands);
    }

    [Fact]
    public void BuildSubmitCommands_KicksAfterJavaBanSoOnlineCrossplayPlayersDisconnect()
    {
        string formattedName = CommandFormatter.FormatPlayerName("Sahaj33", "Paper");

        IReadOnlyList<string> commands = PlayerActionCommandBuilder.BuildSubmitCommands(
            "Ban",
            formattedName,
            "Paper",
            "griefing");

        Assert.Equal(new[] { "ban Sahaj33 griefing", "kick Sahaj33 griefing" }, commands);
    }

    [Fact]
    public void BuildPardonCommands_DoesNotSendNativePardonForBedrockSidecarBans()
    {
        IReadOnlyList<string> commands = PlayerActionCommandBuilder.BuildPardonCommands(
            "\"Sahaj Italiya\"",
            "Bedrock (BDS)");

        Assert.Empty(commands);
    }

    [Fact]
    public void BuildPardonCommands_UsesUnbanForPocketMine()
    {
        IReadOnlyList<string> commands = PlayerActionCommandBuilder.BuildPardonCommands(
            CommandFormatter.FormatPlayerName("Sahaj Italiya", "Pocketmine (PHP)"),
            "Pocketmine (PHP)");

        Assert.Equal(new[] { "unban \"Sahaj Italiya\"" }, commands);
    }
}
