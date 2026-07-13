using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PocketMC.Domain.Storage
{
    public static class PlatformPaths
    {
        public static string GetAppDataRoot()
        {
            return GetAppDataRootInternal(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            );
        }

        public static string GetPlayitConfigRoot()
        {
            return GetPlayitConfigRootInternal(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            );
        }

        internal static string GetAppDataRootInternal(bool isWindows, string? xdgConfigHome, string userProfile, string localAppData)
        {
            if (isWindows)
            {
                return Path.Combine(localAppData, "PocketMC");
            }
            else
            {
                string baseDir = !string.IsNullOrEmpty(xdgConfigHome)
                    ? xdgConfigHome
                    : Path.Combine(userProfile, ".config");
                return Path.Combine(baseDir, "pocketmc");
            }
        }

        internal static string GetPlayitConfigRootInternal(bool isWindows, string? xdgConfigHome, string userProfile, string localAppData)
        {
            if (isWindows)
            {
                return Path.Combine(localAppData, "playit_gg");
            }
            else
            {
                string baseDir = !string.IsNullOrEmpty(xdgConfigHome)
                    ? xdgConfigHome
                    : Path.Combine(userProfile, ".config");
                return Path.Combine(baseDir, "playit_gg");
            }
        }
    }
}
