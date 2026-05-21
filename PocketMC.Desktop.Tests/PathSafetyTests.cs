using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData("mods/worldedit.jar", false)]
    [InlineData("config/settings.yml", false)]
    [InlineData("mods/subfolder/mod.jar", false)]
    [InlineData("../../malware.exe", true)]
    [InlineData("mods/../../../escape.dll", true)]
    [InlineData("..\\windows\\system32\\evil.dll", true)]
    [InlineData("mods/..\\..\\..\\escape.exe", true)]
    [InlineData("mods/server.jar:evil", true)]
    [InlineData("C:\\Windows\\system32\\drivers\\etc\\hosts", true)]
    [InlineData("\\\\?\\C:\\Windows\\system32\\drivers\\etc\\hosts", true)]
    [InlineData("//server/share/file.txt", true)]
    public void ContainsTraversal_DetectsEscapeAttempts(string path, bool shouldBeTraversal)
    {
        Assert.Equal(shouldBeTraversal, PathSafety.ContainsTraversal(path));
    }

    [Fact]
    public void ValidateContainedPath_ReturnsNull_WhenPathEscapesRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "test-root");
        Assert.Null(PathSafety.ValidateContainedPath(root, "../../escape.txt"));
    }

    [Fact]
    public void ValidateContainedPath_ReturnsResolvedPath_WhenContained()
    {
        string root = Path.Combine(Path.GetTempPath(), "test-root");
        string? result = PathSafety.ValidateContainedPath(root, "mods/worldedit.jar");

        Assert.NotNull(result);
        Assert.StartsWith(Path.GetFullPath(root), result!);
        Assert.EndsWith("worldedit.jar", result!);
    }

    [Fact]
    public void ValidateContainedPath_HandlesNestedSubdirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "test-root");
        string? result = PathSafety.ValidateContainedPath(root, "config/mods/advanced/settings.yml");

        Assert.NotNull(result);
        Assert.Contains("advanced", result!);
    }

    [Theory]
    [InlineData("mods/server.jar:payload")]
    [InlineData("C:\\Windows\\system32\\drivers\\etc\\hosts")]
    [InlineData("\\\\?\\C:\\Windows\\system32\\drivers\\etc\\hosts")]
    [InlineData("//server/share/file.txt")]
    public void ValidateContainedPath_ReturnsNull_ForWindowsSpecialPaths(string path)
    {
        string root = Path.Combine(Path.GetTempPath(), "test-root");
        Assert.Null(PathSafety.ValidateContainedPath(root, path));
    }
}
