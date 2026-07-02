using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Application.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Application.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Tests;

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



