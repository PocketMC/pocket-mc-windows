using System;
using System.IO;
using System.Runtime.InteropServices;
using PocketMC.Domain.Storage;
using PocketMC.E2E.Tests.Infrastructure;
using Xunit;

namespace PocketMC.E2E.Tests.Features.Configuration
{
    public class XdgPathTests
    {
        [Fact]
        public void Test1_GetAppDataRootInternal_Windows()
        {
            // Arrange & Act
            var path = PlatformPaths.GetAppDataRootInternal(
                isWindows: true,
                xdgConfigHome: null,
                userProfile: @"C:\Users\Test",
                localAppData: @"C:\Users\Test\AppData\Local"
            );

            // Assert
            Assert.Equal(@"C:\Users\Test\AppData\Local\PocketMC", path);
        }

        [Fact]
        public void Test2_GetAppDataRootInternal_Unix_WithXdg()
        {
            // Arrange & Act
            var path = PlatformPaths.GetAppDataRootInternal(
                isWindows: false,
                xdgConfigHome: "/custom/xdg/config",
                userProfile: "/home/test",
                localAppData: ""
            );

            // Assert
            Assert.Equal("/custom/xdg/config/pocketmc", path);
        }

        [Fact]
        public void Test3_GetAppDataRootInternal_Unix_NoXdg()
        {
            // Arrange & Act
            var path = PlatformPaths.GetAppDataRootInternal(
                isWindows: false,
                xdgConfigHome: null,
                userProfile: "/home/test",
                localAppData: ""
            );

            // Assert
            Assert.Equal("/home/test/.config/pocketmc", path);
        }

        [Fact]
        public void Test4_GetPlayitConfigRootInternal_Windows()
        {
            // Arrange & Act
            var path = PlatformPaths.GetPlayitConfigRootInternal(
                isWindows: true,
                xdgConfigHome: null,
                userProfile: @"C:\Users\Test",
                localAppData: @"C:\Users\Test\AppData\Local"
            );

            // Assert
            Assert.Equal(@"C:\Users\Test\AppData\Local\playit_gg", path);
        }

        [Fact]
        public void Test5_GetPlayitConfigRootInternal_Unix_WithXdg()
        {
            // Arrange & Act
            var path = PlatformPaths.GetPlayitConfigRootInternal(
                isWindows: false,
                xdgConfigHome: "/custom/xdg/config",
                userProfile: "/home/test",
                localAppData: ""
            );

            // Assert
            Assert.Equal("/custom/xdg/config/playit_gg", path);
        }

        [Fact]
        public void Test6_GetPlayitConfigRootInternal_Unix_NoXdg()
        {
            // Arrange & Act
            var path = PlatformPaths.GetPlayitConfigRootInternal(
                isWindows: false,
                xdgConfigHome: null,
                userProfile: "/home/test",
                localAppData: ""
            );

            // Assert
            Assert.Equal("/home/test/.config/playit_gg", path);
        }

        [Fact]
        public void Test7_GetAppDataRoot_ActivePlatform()
        {
            // Arrange & Act
            var path = PlatformPaths.GetAppDataRoot();

            // Assert
            Assert.NotNull(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketMC");
                Assert.Equal(expected, path);
            }
        }

        [Fact]
        public void Test8_GetPlayitConfigRoot_ActivePlatform()
        {
            // Arrange & Act
            var path = PlatformPaths.GetPlayitConfigRoot();

            // Assert
            Assert.NotNull(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "playit_gg");
                Assert.Equal(expected, path);
            }
        }

        [UnixFact]
        public void Test9_UnixXdgPathsEnvVar_SkipsOnWindows()
        {
            // Arrange
            var originalEnv = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var testPath = "/tmp/pocketmc_xdg_test_" + Guid.NewGuid().ToString("N");
            try
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", testPath);

                // Act
                var appDataRoot = PlatformPaths.GetAppDataRoot();
                var playitConfigRoot = PlatformPaths.GetPlayitConfigRoot();

                // Assert
                Assert.Equal(Path.Combine(testPath, "pocketmc"), appDataRoot);
                Assert.Equal(Path.Combine(testPath, "playit_gg"), playitConfigRoot);
            }
            finally
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", originalEnv);
            }
        }

        [UnixFact]
        public void Test10_CaseSensitiveRename_SkipsOnWindows()
        {
            // Arrange
            using (var ctx = new TestDirectoryContext())
            {
                var srcDir = ctx.CreateDirectory("rename_test");
                var destDir = Path.Combine(ctx.TempPath, "Rename_Test");

                // Act
                // On Unix, direct case-only rename should succeed atomically
                Directory.Move(srcDir, destDir);

                // Assert
                Assert.True(Directory.Exists(destDir));
                Assert.False(Directory.Exists(srcDir));
            }
        }
    }
}
