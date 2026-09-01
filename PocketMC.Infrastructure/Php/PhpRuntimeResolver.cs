using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Php
{
    public record PhpReleaseDefinition(
        string Version,
        string DisplayName,
        string Tag,
        string AssetPattern,
        string AssetFileName,
        string TargetPocketMineVersion)
    {
        public string FallbackDownloadUrl
        {
            get
            {
                string baseReleases = PocketMC.Infrastructure.Configuration.AppConfig.ProviderPhpReleases;
                string directDownloadBase = baseReleases.Replace("api.github.com/repos", "github.com");
                return $"{directDownloadBase}/download/{Tag}/{AssetFileName}";
            }
        }
    }

    public static class PhpRuntimeResolver
    {
        public const string DefaultPhpVersion = "8.2";

        private static readonly IReadOnlyList<PhpReleaseDefinition> ReleaseDefinitions = new List<PhpReleaseDefinition>
        {
            new(
                Version: "8.2",
                DisplayName: "PHP 8.2 (PocketMine-MP 5.x - Recommended)",
                Tag: "pm5-php-8.2-latest",
                AssetPattern: "Windows-x64-PM5",
                AssetFileName: "PHP-8.2-Windows-x64-PM5.zip",
                TargetPocketMineVersion: "PocketMine-MP 5.x"
            ),
            new(
                Version: "8.3",
                DisplayName: "PHP 8.3 (PocketMine-MP 5.x / 6.x)",
                Tag: "pm5-php-8.3-latest",
                AssetPattern: "Windows-x64-PM5",
                AssetFileName: "PHP-8.3-Windows-x64-PM5.zip",
                TargetPocketMineVersion: "PocketMine-MP 5.x / 6.x"
            ),
            new(
                Version: "8.0",
                DisplayName: "PHP 8.0 (PocketMine-MP 4.x Legacy)",
                Tag: "pm4-php-8.0-latest",
                AssetPattern: "Windows-x64-PM4",
                AssetFileName: "PHP-Windows-x64-PM4.zip",
                TargetPocketMineVersion: "PocketMine-MP 4.x"
            ),
            new(
                Version: "8.1",
                DisplayName: "PHP 8.1 (PocketMine-MP 4.x Legacy)",
                Tag: "pm4-php-8.1-latest",
                AssetPattern: "Windows-x64-PM4",
                AssetFileName: "PHP-Windows-x64-PM4.zip",
                TargetPocketMineVersion: "PocketMine-MP 4.x"
            )
        };

        public static IReadOnlyList<string> GetBundledPhpVersions()
        {
            return ReleaseDefinitions.Select(r => r.Version).ToList();
        }

        public static IReadOnlyList<PhpReleaseDefinition> GetReleaseDefinitions()
        {
            return ReleaseDefinitions;
        }

        public static PhpReleaseDefinition? GetDefinition(string version)
        {
            return ReleaseDefinitions.FirstOrDefault(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetRequiredPhpVersion(InstanceMetadata meta)
        {
            return GetRequiredPhpVersion(meta.MinecraftVersion);
        }

        public static string GetRequiredPhpVersion(string? pocketMineVersion)
        {
            if (string.IsNullOrWhiteSpace(pocketMineVersion))
            {
                return DefaultPhpVersion;
            }

            string clean = pocketMineVersion.Trim().TrimStart('v', 'V');

            if (clean.StartsWith("4.", StringComparison.OrdinalIgnoreCase))
            {
                return "8.0";
            }

            if (clean.StartsWith("6.", StringComparison.OrdinalIgnoreCase))
            {
                return "8.3";
            }

            if (clean.StartsWith("5.", StringComparison.OrdinalIgnoreCase))
            {
                return "8.2";
            }

            return DefaultPhpVersion;
        }
    }
}
