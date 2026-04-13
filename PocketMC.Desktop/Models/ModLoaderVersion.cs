using System.Collections.Generic;

namespace PocketMC.Desktop.Models
{
    public class ModLoaderVersion
    {
        public string Version { get; set; } = string.Empty;
        public bool IsStable { get; set; } = true;

        public override string ToString() => Version + (IsStable ? "" : " (experimental)");
    }

    public class GameVersionWithLoaders : MinecraftVersion
    {
        public List<ModLoaderVersion> LoaderVersions { get; set; } = new List<ModLoaderVersion>();
    }
}
