namespace PocketMC.Desktop.Tests;

public sealed class MarketplaceDownloadPathSafetyTests
{
    [Theory]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Marketplace", "PluginBrowserPage.xaml.cs" },
        "Path.Combine(destDir, fileName)",
        "MarketplaceFileNameSanitizer.RequireSafeFileName(fileName)")]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Marketplace", "MapBrowserPage.xaml.cs" },
        "Path.Combine(Path.GetTempPath(), file.FileName)",
        "MarketplaceFileNameSanitizer.RequireSafeFileName(file.FileName)")]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Marketplace", "AddonUpdateService.cs" },
        "Path.Combine(destDir, updateInfo.LatestFileName)",
        "MarketplaceFileNameSanitizer.RequireSafeFileName(updateInfo.LatestFileName)")]
    public void MarketplaceDownloadWriters_NormalizeProviderFileNamesBeforeCombiningPaths(
        string[] sourcePath,
        string unsafePathCombine,
        string expectedSanitizerCall)
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(sourcePath));

        Assert.DoesNotContain(unsafePathCombine, source);
        Assert.Contains(expectedSanitizerCall, source);
    }
}
