using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class ServerSettingsProfileTests
{
    [Theory]
    [InlineData("Vanilla", true, false, false, "25565", "motd")]
    [InlineData("Paper", true, false, false, "25565", "motd")]
    [InlineData("Bedrock", false, true, false, "19132", "server-name")]
    [InlineData("Pocketmine-MP", false, false, true, "19132", "server-name")]
    public void FromMetadata_SelectsPerspectiveSpecificCapabilities(
        string serverType,
        bool isJava,
        bool isBedrockDedicated,
        bool isPocketMine,
        string defaultPort,
        string displayNamePropertyKey)
    {
        var profile = ServerSettingsProfile.FromMetadata(new InstanceMetadata { ServerType = serverType }, false);

        Assert.Equal(isJava, profile.IsJava);
        Assert.Equal(isBedrockDedicated, profile.IsBedrockDedicated);
        Assert.Equal(isPocketMine, profile.IsPocketMine);
        Assert.Equal(defaultPort, profile.DefaultServerPort);
        Assert.Equal(displayNamePropertyKey, profile.DisplayNamePropertyKey);
    }

    [Fact]
    public void JavaProfile_EnablesJavaOnlySections()
    {
        var profile = ServerSettingsProfile.FromMetadata(new InstanceMetadata { ServerType = "Forge" }, false);

        Assert.True(profile.SupportsJavaRuntimeSettings);
        Assert.True(profile.SupportsJavaWorldGenerator);
        Assert.True(profile.SupportsNether);
        Assert.False(profile.SupportsBedrockRules);
    }

    [Fact]
    public void BedrockProfile_EnablesBedrockRulesAndHidesJavaOnlySections()
    {
        var profile = ServerSettingsProfile.FromMetadata(new InstanceMetadata { ServerType = "Bedrock Dedicated Server" }, false);

        Assert.False(profile.SupportsJavaRuntimeSettings);
        Assert.False(profile.SupportsJavaWorldGenerator);
        Assert.False(profile.SupportsNether);
        Assert.True(profile.SupportsBedrockRules);
    }
}


