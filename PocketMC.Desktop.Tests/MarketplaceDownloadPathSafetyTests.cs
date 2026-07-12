namespace PocketMC.Desktop.Tests;

public sealed class MarketplaceDownloadPathSafetyTests
{
    [Theory]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Marketplace", "PluginBrowserPage.xaml.cs" },
        "Path.Combine(destDir, fileName)",
        "MarketplaceDownloadPolicy.RequireCompatibleFileName(fileName")]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Marketplace", "MapBrowserPage.xaml.cs" },
        "Path.Combine(Path.GetTempPath(), file.FileName)",
        "MarketplaceFileNameSanitizer.RequireSafeFileName(file.FileName)")]
    [InlineData(
        new[] { "PocketMC.Infrastructure", "Marketplace", "AddonUpdateService.cs" },
        "Path.Combine(destDir, updateInfo.LatestFileName)",
        "MarketplaceDownloadPolicy.RequireCompatibleFileName(updateInfo.LatestFileName")]
    public void MarketplaceDownloadWriters_NormalizeProviderFileNamesBeforeCombiningPaths(
        string[] sourcePath,
        string unsafePathCombine,
        string expectedSanitizerCall)
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(sourcePath));

        Assert.DoesNotContain(unsafePathCombine, source);
        Assert.Contains(expectedSanitizerCall, source);
    }

    [Theory]
    [InlineData("PluginBrowserPage.xaml.cs")]
    [InlineData("AddonUpdateService.cs")]
    public void MarketplaceInstallAndUpdatePaths_UseSafeInstallerInsteadOfDirectFileStream(string fileName)
    {
        string[] sourcePath = fileName == "AddonUpdateService.cs"
            ? new[] { "PocketMC.Infrastructure", "Marketplace", fileName }
            : new[] { "PocketMC.Desktop", "Features", "Marketplace", fileName };

        string source = File.ReadAllText(TestSourceFileResolver.Resolve(sourcePath));

        Assert.Contains("MarketplaceFileInstaller", source);
        Assert.DoesNotContain("new FileStream", source);
        Assert.DoesNotContain("FileMode.Create", source);
    }
}
