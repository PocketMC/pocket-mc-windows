using System;
using PocketMC.Desktop.Localization;

namespace PocketMC.Desktop.Features.Marketplace
{
    public static class MarketplaceFilterFormatter
    {
        public static string FormatCompatibilityLabel(string? serverType, string? mcVersion)
        {
            string versionText;
            string cleanVersion = CleanVersionString(mcVersion);

            if (string.IsNullOrWhiteSpace(cleanVersion) || cleanVersion == "*")
            {
                versionText = L.T("All Versions");
            }
            else
            {
                versionText = L.F("Minecraft {0}", cleanVersion);
            }

            if (string.IsNullOrWhiteSpace(serverType))
            {
                return versionText;
            }

            return $"{serverType.Trim()} • {versionText}";
        }

        public static string FormatCompatibilityTooltip(string? serverType, string? mcVersion)
        {
            string cleanVersion = CleanVersionString(mcVersion);
            bool hasServerType = !string.IsNullOrWhiteSpace(serverType);
            bool hasSpecificVersion = !string.IsNullOrWhiteSpace(cleanVersion) && cleanVersion != "*";

            if (hasServerType && hasSpecificVersion)
            {
                return L.F("Filtered for {0} compatibility on Minecraft {1}", serverType!.Trim(), cleanVersion);
            }

            if (hasServerType)
            {
                return L.F("Filtered for {0} compatibility on all Minecraft versions", serverType!.Trim());
            }

            if (hasSpecificVersion)
            {
                return L.F("Filtered for Minecraft {0} compatibility", cleanVersion);
            }

            return L.T("Showing items for all versions");
        }

        private static string CleanVersionString(string? mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion)) return string.Empty;
            string trimmed = mcVersion.Trim();
            if (trimmed.StartsWith("Minecraft ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("Minecraft ".Length).Trim();
            }
            return trimmed;
        }
    }
}
