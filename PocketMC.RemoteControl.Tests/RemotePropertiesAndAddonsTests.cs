using System;
using PocketMC.RemoteControl.Models;
using Xunit;

namespace PocketMC.RemoteControl.Tests;

public class RemotePropertiesAndAddonsTests
{
    [Fact]
    public void RemoteServerPropertiesDto_DefaultsAndProperties_AreSet()
    {
        var dto = new RemoteServerPropertiesDto
        {
            Motd = "Test Motd",
            Gamemode = "creative",
            Difficulty = "hard",
            MaxPlayers = 50,
            Pvp = false
        };

        Assert.Equal("Test Motd", dto.Motd);
        Assert.Equal("creative", dto.Gamemode);
        Assert.Equal("hard", dto.Difficulty);
        Assert.Equal(50, dto.MaxPlayers);
        Assert.False(dto.Pvp);
    }

    [Fact]
    public void RemoteAddonDto_Properties_MapCorrectly()
    {
        var dto = new RemoteAddonDto
        {
            Name = "EssentialsX.jar",
            FilePath = "plugins/EssentialsX.jar",
            SizeKb = 1200.5,
            LastModified = "2026-08-10T12:00:00Z",
            AddonType = "plugin"
        };

        Assert.Equal("EssentialsX.jar", dto.Name);
        Assert.Equal("plugins/EssentialsX.jar", dto.FilePath);
        Assert.Equal(1200.5, dto.SizeKb);
        Assert.Equal("plugin", dto.AddonType);
    }
}
