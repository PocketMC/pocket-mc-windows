using PocketMC.Application.Services.Setup;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Tests.Services.Setup;

public sealed class RootDirectorySetupHelperTests
{
    [Theory]
    [InlineData(@"C:\Users\Sahaj\Documents", @"C:\Users\Sahaj\Documents\PocketMC")]
    [InlineData(@"D:\Games\PocketMC", @"D:\Games\PocketMC")]
    [InlineData(@"D:\Games\PocketMC\", @"D:\Games\PocketMC\")]
    [InlineData(@"E:\Servers\pocketmc", @"E:\Servers\pocketmc")]
    public void ResolveRootPath_UsesPocketMcFolderNameConsistently(string selectedPath, string expectedRootPath)
    {
        Assert.Equal(expectedRootPath, RootDirectorySetupHelper.ResolveRootPath(selectedPath));
    }

    [Fact]
    public void ResolveRootPath_RejectsBlankSelection()
    {
        Assert.Throws<ArgumentException>(() => RootDirectorySetupHelper.ResolveRootPath(" "));
    }
}


