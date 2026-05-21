using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Tests;

public sealed class MarketplaceFileNameSanitizerTests
{
    [Theory]
    [InlineData("mod.jar", "mod.jar")]
    [InlineData("folder/mod.jar", "mod.jar")]
    [InlineData("..\\outside.jar", "outside.jar")]
    [InlineData("C:\\Temp\\outside.jar", "outside.jar")]
    public void RequireSafeFileName_ReturnsLeafName(string input, string expected)
    {
        Assert.Equal(expected, MarketplaceFileNameSanitizer.RequireSafeFileName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("CON.jar")]
    [InlineData("NUL.zip")]
    [InlineData("LPT1.jar")]
    [InlineData("mod.jar ")]
    [InlineData("mod.jar.")]
    [InlineData("server.jar:ads")]
    public void RequireSafeFileName_ThrowsForInvalidLeafNames(string input)
    {
        Assert.Throws<InvalidOperationException>(() => MarketplaceFileNameSanitizer.RequireSafeFileName(input));
    }
}
