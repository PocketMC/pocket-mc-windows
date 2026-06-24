using System.IO;
using System.Linq;

namespace PocketMC.Desktop.Helpers;

public class GeyserDetector : IGeyserDetector
{
    public bool IsGeyserInstalled(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            return false;
        }

        try
        {
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
