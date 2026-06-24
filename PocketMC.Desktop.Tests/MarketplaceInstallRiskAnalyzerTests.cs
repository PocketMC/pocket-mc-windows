using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Tests;

public sealed class MarketplaceInstallRiskAnalyzerTests
{
    [Fact]
    public void CurseForgeModInstall_ExposesServerCompatibilityWarning()
    {
        MarketplaceInstallRisk risk = MarketplaceInstallRiskAnalyzer.Analyze(
            providerName: "CurseForge",
            projectType: "project_type:mod",
            projectTitle: "Example Mod",
            fileName: "example.jar");

        Assert.Contains(risk.Warnings, warning => warning.Contains("CurseForge does not provide reliable server-side metadata", StringComparison.OrdinalIgnoreCase));
        Assert.True(risk.RequiresConfirmation);
    }

    [Theory]
    [InlineData("sodium")]
    [InlineData("iris")]
    [InlineData("journeymap")]
    [InlineData("xaero-minimap")]
    [InlineData("replaymod")]
    public void ObviousClientOnlyNames_RequireConfirmation(string name)
    {
        MarketplaceInstallRisk risk = MarketplaceInstallRiskAnalyzer.Analyze(
            providerName: "CurseForge",
            projectType: "project_type:mod",
            projectTitle: name,
            fileName: $"{name}.jar");

        Assert.True(risk.RequiresConfirmation);
        Assert.Contains(risk.Warnings, warning => warning.Contains("client-only", StringComparison.OrdinalIgnoreCase));
    }
}
