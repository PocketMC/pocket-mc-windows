using PocketMC.Desktop.Features.Marketplace;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Marketplace;

public sealed class MarketplaceFilterFormatterTests
{
    [Fact]
    public void FormatCompatibilityLabel_WithServerTypeAndMinecraftVersion_CombinesBoth()
    {
        string label = MarketplaceFilterFormatter.FormatCompatibilityLabel("Paper", "1.20.1");

        Assert.Equal("Paper • Minecraft 1.20.1", label);
    }

    [Theory]
    [InlineData("Fabric", "*", "Fabric • All Versions")]
    [InlineData("Forge", "", "Forge • All Versions")]
    [InlineData("NeoForge", null, "NeoForge • All Versions")]
    [InlineData("Paper", "Minecraft 1.20.1", "Paper • Minecraft 1.20.1")]
    [InlineData(null, "1.20.1", "Minecraft 1.20.1")]
    [InlineData("", "1.20.1", "Minecraft 1.20.1")]
    [InlineData("   ", "1.20.1", "Minecraft 1.20.1")]
    [InlineData(null, "*", "All Versions")]
    [InlineData(null, null, "All Versions")]
    public void FormatCompatibilityLabel_HandlesVariousInputsCorrectly(string? serverType, string? mcVersion, string expected)
    {
        string label = MarketplaceFilterFormatter.FormatCompatibilityLabel(serverType, mcVersion);

        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("Paper", "1.20.1", "Filtered for Paper compatibility on Minecraft 1.20.1")]
    [InlineData("Fabric", "*", "Filtered for Fabric compatibility on all Minecraft versions")]
    [InlineData(null, "1.20.1", "Filtered for Minecraft 1.20.1 compatibility")]
    [InlineData(null, "*", "Showing items for all versions")]
    public void FormatCompatibilityTooltip_GeneratesClearHelpText(string? serverType, string? mcVersion, string expected)
    {
        string tooltip = MarketplaceFilterFormatter.FormatCompatibilityTooltip(serverType, mcVersion);

        Assert.Equal(expected, tooltip);
    }
}
