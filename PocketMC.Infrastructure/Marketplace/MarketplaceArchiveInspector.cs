using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PocketMC.Infrastructure.Marketplace;

public static class MarketplaceArchiveInspector
{
    public static IReadOnlyList<string> InspectServerCompatibilityWarnings(string filePath, bool isPlugin = false)
    {
        string extension = Path.GetExtension(filePath);
        if (!extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(filePath);
            var warnings = new List<string>();

            InspectFabricLikeManifest(archive, "fabric.mod.json", "Fabric", warnings);
            InspectFabricLikeManifest(archive, "quilt.mod.json", "Quilt", warnings);
            InspectForgeLikeManifest(archive, warnings);
            InspectPluginManifest(archive, isPlugin, warnings);

            return warnings;
        }
        catch (InvalidDataException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static bool IsClientOnlyAddon(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        if (!extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(filePath);
            var warnings = new List<string>();

            InspectFabricLikeManifest(archive, "fabric.mod.json", "Fabric", warnings);
            InspectFabricLikeManifest(archive, "quilt.mod.json", "Quilt", warnings);
            InspectForgeLikeManifest(archive, warnings);

            return warnings.Any(w => w.Contains("client only", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PocketMC could not inspect add-on archive {filePath}: {ex}");
            return false;
        }
    }


    private static void InspectFabricLikeManifest(
        ZipArchive archive,
        string manifestName,
        string loaderName,
        List<string> warnings)
    {
        ZipArchiveEntry? entry = archive.GetEntry(manifestName);
        if (entry == null)
        {
            return;
        }

        using Stream stream = entry.Open();
        JsonNode? manifest = JsonNode.Parse(stream);
        string? environment = manifest?["environment"]?.ToString();
        if (environment != null && environment.Equals("client", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"client only mod");
        }
    }

    private static void InspectForgeLikeManifest(ZipArchive archive, List<string> warnings)
    {
        ZipArchiveEntry? modsTomlEntry = archive.GetEntry("META-INF/mods.toml") ?? archive.GetEntry("META-INF/neoforge.mods.toml");
        if (modsTomlEntry == null)
        {
            return;
        }

        try
        {
            using Stream stream = modsTomlEntry.Open();
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd();

            bool isClientOnly = Regex.IsMatch(content, @"clientSideOnly\s*=\s*true", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

            if (isClientOnly)
            {
                warnings.Add("client only mod");
            }
        }
        catch (Exception ex)
        {
            // Do not crash on unreadable mods.toml
            Debug.WriteLine($"PocketMC could not inspect mods.toml in add-on archive: {ex}");
        }
    }

    private static void InspectPluginManifest(ZipArchive archive, bool isPlugin, List<string> warnings)
    {
        if (isPlugin)
        {
            bool hasPluginYml = archive.GetEntry("plugin.yml") != null || archive.GetEntry("paper-plugin.yml") != null;
            if (!hasPluginYml)
            {
                warnings.Add("Plugin metadata (plugin.yml or paper-plugin.yml) is missing. This file might not be a valid plugin.");
            }
        }
    }
}
