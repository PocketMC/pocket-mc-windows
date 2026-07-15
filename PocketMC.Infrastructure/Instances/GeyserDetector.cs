using System;
using System.IO;
using System.Linq;

using PocketMC.Application.Interfaces.Instances;
using PocketMC.Infrastructure.Marketplace;

namespace PocketMC.Infrastructure.Instances;

public class GeyserDetector : IGeyserDetector
{
    private readonly AddonManifestService _manifestService;

    public GeyserDetector(AddonManifestService manifestService)
    {
        _manifestService = manifestService;
    }

    public bool IsGeyserInstalled(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            return false;
        }

        try
        {
            // 1. Try manifest first
            var manifest = _manifestService.LoadManifest(instancePath);
            if (manifest.Entries.Any(e => 
                (e.ProjectSlug != null && e.ProjectSlug.Equals("geyser", StringComparison.OrdinalIgnoreCase)) ||
                (e.ProjectTitle != null && e.ProjectTitle.Contains("Geyser", StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            // 2. Fallback to jar scan
            string pluginsDir = Path.Combine(instancePath, "plugins");
            string modsDir = Path.Combine(instancePath, "mods");

            bool geyserInPlugins = Directory.Exists(pluginsDir) &&
                                   Directory.EnumerateFiles(pluginsDir, "Geyser*.jar", SearchOption.TopDirectoryOnly).Any();

            bool geyserInMods = Directory.Exists(modsDir) &&
                                 Directory.EnumerateFiles(modsDir, "Geyser*.jar", SearchOption.TopDirectoryOnly).Any();

            return geyserInPlugins || geyserInMods;
        }
        catch
        {
            return false;
        }
    }
}
