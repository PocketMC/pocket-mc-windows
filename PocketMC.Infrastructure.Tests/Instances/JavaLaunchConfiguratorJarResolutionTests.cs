using System;
using System.IO;
using PocketMC.Infrastructure.Instances;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances;

public sealed class JavaLaunchConfiguratorJarResolutionTests : IDisposable
{
    private readonly string _tempDirectory;

    public JavaLaunchConfiguratorJarResolutionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC_JarResolution_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_tempDirectory, name), "fake");

    [Fact]
    public void ResolveStartableJar_PrefersServerJar()
    {
        Touch("server.jar");
        Touch("mohist-1.20.1-101-server.jar");

        Assert.Equal("server.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_UsesMohistJarWhenServerJarMissing()
    {
        Touch("mohist-1.20.1-101-server.jar");

        Assert.Equal("mohist-1.20.1-101-server.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_IgnoresInstallerJars()
    {
        Touch("forge-1.20.1-47.2.0-installer.jar");
        Touch("arclight-forge-1.20.1-1.0.2.jar");

        Assert.Equal("arclight-forge-1.20.1-1.0.2.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_PrefersForgeJarForForgeInstalls()
    {
        Touch("server.jar");
        Touch("forge-1.20.1-47.2.0-universal.jar");

        Assert.Equal("forge-1.20.1-47.2.0-universal.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: true));
    }

    [Fact]
    public void ResolveStartableJar_ReturnsSingleUnknownJar()
    {
        Touch("custom-server-build.jar");

        Assert.Equal("custom-server-build.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_ReturnsNullWhenAmbiguous()
    {
        Touch("alpha.jar");
        Touch("beta.jar");

        Assert.Null(JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveWinArgsPath_PrefersTheNewestLoaderInstall()
    {
        string oldArgs = Path.Combine(_tempDirectory, "libraries", "net", "neoforged", "neoforge", "21.1.100", "win_args.txt");
        string newArgs = Path.Combine(_tempDirectory, "libraries", "net", "neoforged", "neoforge", "21.1.172", "win_args.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(oldArgs)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newArgs)!);
        File.WriteAllText(oldArgs, "@user_jvm_args.txt");
        File.WriteAllText(newArgs, "@user_jvm_args.txt");
        File.SetLastWriteTimeUtc(oldArgs, DateTime.UtcNow.AddDays(-3));

        string? picked = JavaLaunchConfigurator.ResolveWinArgsPath(_tempDirectory);

        Assert.Equal(newArgs, picked);
    }

    [Fact]
    public void ResolveWinArgsPath_PrefersLoaderLibrariesOverStrayCopies()
    {
        string stray = Path.Combine(_tempDirectory, "old-backup", "win_args.txt");
        string loader = Path.Combine(_tempDirectory, "libraries", "net", "minecraftforge", "forge", "1.20.1-47.2.0", "win_args.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        Directory.CreateDirectory(Path.GetDirectoryName(loader)!);
        File.WriteAllText(stray, "stale");
        File.WriteAllText(loader, "@user_jvm_args.txt");
        File.SetLastWriteTimeUtc(loader, DateTime.UtcNow.AddDays(-10));

        string? picked = JavaLaunchConfigurator.ResolveWinArgsPath(_tempDirectory);

        Assert.Equal(loader, picked);
    }
}
