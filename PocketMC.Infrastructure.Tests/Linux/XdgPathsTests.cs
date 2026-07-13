using PocketMC.Application.Services.Instances;
using System.IO;
using System.Runtime.InteropServices;

namespace PocketMC.Infrastructure.Tests.Linux;

/// <summary>
/// TDD tests for XDG-compliant path resolution and atomic directory moves.
/// Linux-specific tests are gated behind OperatingSystem.IsLinux().
/// </summary>
public class XdgPathsTests
{
    [Fact]
    public void GetXdgConfigPath_OnWindows_ReturnsLocalAppData()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = XdgPaths.GetConfigBasePath("pocketmc");

        Assert.Contains("AppData", path, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void GetXdgConfigPath_OnLinux_UsesXdgConfigHomeOrFallback()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        // XDG_CONFIG_HOME not set — should fall back to ~/.config/pocketmc
        string? savedVar = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
        try
        {
            string path = XdgPaths.GetConfigBasePath("pocketmc");
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "pocketmc");
            Assert.Equal(expected, path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", savedVar);
        }
    }

    [SkippableFact]
    public void GetXdgConfigPath_OnLinux_RespectsXdgConfigHomeVar()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        string? savedVar = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/tmp/test-config");
        try
        {
            string path = XdgPaths.GetConfigBasePath("pocketmc");
            Assert.Equal(Path.Combine("/tmp/test-config", "pocketmc"), path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", savedVar);
        }
    }

    [SkippableFact]
    public void GetXdgDataPath_OnLinux_UsesXdgDataHomeOrFallback()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        string? savedVar = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        try
        {
            string path = XdgPaths.GetDataBasePath("pocketmc");
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "pocketmc");
            Assert.Equal(expected, path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", savedVar);
        }
    }

    [SkippableFact]
    public void AtomicMoveDirectory_CaseSensitive_SucceedsWithoutTempStep()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        string tempRoot = Path.Combine(Path.GetTempPath(), $"pocketmc-test-{Guid.NewGuid():N}");
        string srcDir = Path.Combine(tempRoot, "myserver");
        string dstDir = Path.Combine(tempRoot, "MyServer");   // different case — different dir on Linux

        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "test.txt"), "content");

        try
        {
            XdgPaths.AtomicMoveDirectory(srcDir, dstDir);

            Assert.False(Directory.Exists(srcDir));
            Assert.True(Directory.Exists(dstDir));
            Assert.True(File.Exists(Path.Combine(dstDir, "test.txt")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AtomicMoveDirectory_Throws_WhenDestinationAlreadyExists()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"pocketmc-test-{Guid.NewGuid():N}");
        string srcDir = Path.Combine(tempRoot, "src");
        string dstDir = Path.Combine(tempRoot, "dst");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);

        try
        {
            Assert.Throws<IOException>(() => XdgPaths.AtomicMoveDirectory(srcDir, dstDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }
}
