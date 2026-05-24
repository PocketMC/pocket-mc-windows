using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PocketMC.Desktop.Features.Marketplace;

public static class MarketplaceArchiveInspector
{
    public static IReadOnlyList<string> InspectServerCompatibilityWarnings(string filePath)
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
            warnings.Add($"{loaderName} metadata marks this add-on as client-only. It may crash or be ignored by a server.");
        }
    }
}
