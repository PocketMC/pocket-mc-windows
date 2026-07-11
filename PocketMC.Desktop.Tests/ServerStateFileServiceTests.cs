using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Services.Players;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class ServerStateFileServiceTests
{
    [Fact]
    public async Task GetBannedPlayersAsync_ReadsPocketMineBannedPlayersFile()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        InstanceMetadata instance = workspace.CreateInstance("PocketMine", "Pocketmine (PHP)");
        workspace.WriteFile(instance.Id, "banned-players.txt", "SahajItaliya\r\n");

        var service = new ServerStateFileService(
            workspace.Registry,
            NullLogger<ServerStateFileService>.Instance);

        List<BannedPlayerEntry> bans = await service.GetBannedPlayersAsync(instance);

        BannedPlayerEntry ban = Assert.Single(bans);
        Assert.Equal("SahajItaliya", ban.Name);
        Assert.Equal("forever", ban.Expires);
    }

    [Fact]
    public async Task GetBannedPlayersAsync_StillReadsLegacyPocketMineBannedFile()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        InstanceMetadata instance = workspace.CreateInstance("PocketMine", "Pocketmine (PHP)");
        workspace.WriteFile(instance.Id, "banned.txt", "LegacyPlayer\r\n");

        var service = new ServerStateFileService(
            workspace.Registry,
            NullLogger<ServerStateFileService>.Instance);

        List<BannedPlayerEntry> bans = await service.GetBannedPlayersAsync(instance);

        BannedPlayerEntry ban = Assert.Single(bans);
        Assert.Equal("LegacyPlayer", ban.Name);
    }
}


