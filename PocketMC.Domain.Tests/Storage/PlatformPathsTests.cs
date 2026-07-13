using System;
using System.IO;
using PocketMC.Domain.Storage;
using Xunit;

namespace PocketMC.Domain.Tests.Storage
{
    public class PlatformPathsTests
    {
        [Fact]
        public void GetAppDataRootInternal_OnWindows_ReturnsLocalAppDataPocketMc()
        {
            string result = PlatformPaths.GetAppDataRootInternal(
                isWindows: true,
                xdgConfigHome: "/home/user/.config",
                userProfile: "/home/user",
                localAppData: "/appdata/local"
            );
            Assert.Equal(Path.Combine("/appdata/local", "PocketMC"), result);
        }

        [Fact]
        public void GetAppDataRootInternal_OnUnix_WithXdgConfigHome_ReturnsXdgPocketMc()
        {
            string result = PlatformPaths.GetAppDataRootInternal(
                isWindows: false,
                xdgConfigHome: "/custom/xdg/config",
                userProfile: "/home/user",
                localAppData: "/appdata/local"
            );
            Assert.Equal(Path.Combine("/custom/xdg/config", "pocketmc"), result);
        }

        [Fact]
        public void GetAppDataRootInternal_OnUnix_WithoutXdgConfigHome_ReturnsUserProfileFallback()
        {
            string result = PlatformPaths.GetAppDataRootInternal(
                isWindows: false,
                xdgConfigHome: null,
                userProfile: "/home/user",
                localAppData: "/appdata/local"
            );
            Assert.Equal(Path.Combine("/home/user", ".config", "pocketmc"), result);
        }

        [Fact]
        public void GetPlayitConfigRootInternal_OnWindows_ReturnsLocalAppDataPlayitGg()
        {
            string result = PlatformPaths.GetPlayitConfigRootInternal(
                isWindows: true,
                xdgConfigHome: "/home/user/.config",
                userProfile: "/home/user",
                localAppData: "/appdata/local"
            );
            Assert.Equal(Path.Combine("/appdata/local", "playit_gg"), result);
        }

        [Fact]
        public void GetPlayitConfigRootInternal_OnUnix_WithXdgConfigHome_ReturnsXdgPlayitGg()
        {
            string result = PlatformPaths.GetPlayitConfigRootInternal(
                isWindows: false,
                xdgConfigHome: "/custom/xdg/config",
                userProfile: "/home/user",
                localAppData: "/appdata/local"
            );
            Assert.Equal(Path.Combine("/custom/xdg/config", "playit_gg"), result);
        }

        [Fact]
        public void GetPlayitConfigRootInternal_OnUnix_WithoutXdgConfigHome_ReturnsUserProfileFallback()
        {
            string result = PlatformPaths.GetPlayitConfigRootInternal(
                isWindows: false,
                xdgConfigHome: null,
                userProfile: "/home/user",
                localAppData: "/appdata/local"
            );
            Assert.Equal(Path.Combine("/home/user", ".config", "playit_gg"), result);
        }
    }
}
