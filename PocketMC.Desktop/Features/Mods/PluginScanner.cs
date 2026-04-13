using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Mods
{
    /// <summary>
    /// Reads plugin.yml from inside a plugin JAR (which is a ZIP) to extract
    /// metadata for compatibility checking against the server version.
    /// </summary>
    public static class PluginScanner
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        private static readonly Regex ApiVersionRegex = new(
            @"api-version:\s*['""]?([^\s'""]+)['""]?",
            RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex PluginNameRegex = new(
            @"^name:\s*['""]?([^\s'""]+)['""]?",
            RegexOptions.Compiled | RegexOptions.Multiline,
            RegexTimeout);

        /// <summary>
        /// Attempts to read the api-version from plugin.yml inside a JAR file.
        /// Returns null if not found or not a valid plugin JAR.
        /// </summary>
        public static string? TryGetApiVersion(string jarPath)
        {
            try
            {
                string? yaml = ReadPluginYaml(jarPath);
                if (yaml == null) return null;
                var match = ApiVersionRegex.Match(yaml);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to read the plugin name from plugin.yml.
        /// </summary>
        public static string? TryGetPluginName(string jarPath)
        {
            try
            {
                string? yaml = ReadPluginYaml(jarPath);
                if (yaml == null) return null;
                var match = PluginNameRegex.Match(yaml);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Determines if a plugin's api-version is incompatible with the server's Minecraft version.
        /// </summary>
        /// <returns>true if incompatible (plugin too new for server), false if compatible or unknown</returns>
        public static bool IsIncompatible(string? pluginApiVersion, string? serverMinecraftVersion)
        {
            if (string.IsNullOrEmpty(pluginApiVersion) || string.IsNullOrEmpty(serverMinecraftVersion))
                return false;

            try
            {
                var pluginVer = ParseMajorMinor(pluginApiVersion);
                var serverVer = ParseMajorMinor(serverMinecraftVersion);

                if (pluginVer == null || serverVer == null)
                    return false;

                return pluginVer.Value.major > serverVer.Value.major ||
                       (pluginVer.Value.major == serverVer.Value.major && pluginVer.Value.minor > serverVer.Value.minor);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static (int major, int minor)? ParseMajorMinor(string version)
        {
            version = version.Trim().Trim('\'', '"');

            var parts = version.Split('.');
            if (parts.Length < 2) return null;

            if (int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
                return (major, minor);

            return null;
        }

        private static string? ReadPluginYaml(string jarPath)
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.GetEntry("plugin.yml");
            if (entry == null) return null;

            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
    }
}
