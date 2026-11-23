using System;

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
                versionText = "All Versions";
            }
            else
            {
                versionText = $"Minecraft {cleanVersion}";
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
                return $"Filtered for {serverType!.Trim()} compatibility on Minecraft {cleanVersion}";
            }

            if (hasServerType)
            {
                return $"Filtered for {serverType!.Trim()} compatibility on all Minecraft versions";
            }

            if (hasSpecificVersion)
            {
                return $"Filtered for Minecraft {cleanVersion} compatibility";
            }

            return "Showing items for all versions";
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
