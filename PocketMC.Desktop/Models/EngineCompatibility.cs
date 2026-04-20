using System;

namespace PocketMC.Desktop.Models
{
    public enum EngineFamily
    {
        Vanilla,
        Spigot,
        Fabric,
        Forge,
        NeoForge,
        Bedrock,
        Pocketmine
    }

    public class EngineCompatibility
    {
        public EngineFamily Family { get; }
        public string ServerType { get; }

        public bool SupportsPlugins { get; }
        public bool SupportsMods { get; }
        public bool SupportsModpacks { get; }
        public bool SupportsBedrockAddons { get; }

        public bool SupportsModrinth { get; }
        public bool SupportsCurseForge { get; }
        public bool SupportsPoggit { get; }

        public string PrimaryAddonSubDir { get; }
        public string LoaderName { get; }

        public EngineCompatibility(string serverType)
        {
            ServerType = serverType ?? "Vanilla";
            
            if (ServerType.StartsWith("Paper", StringComparison.OrdinalIgnoreCase) || 
                ServerType.StartsWith("Spigot", StringComparison.OrdinalIgnoreCase))
            {
                Family = EngineFamily.Spigot;
                SupportsPlugins = true;
                SupportsModrinth = true;
                SupportsCurseForge = true;
                PrimaryAddonSubDir = "plugins";
                LoaderName = "spigot"; // Modrinth uses spigot/paper/bukkit
            }
            else if (ServerType.StartsWith("Fabric", StringComparison.OrdinalIgnoreCase))
            {
                Family = EngineFamily.Fabric;
                SupportsMods = true;
                SupportsModpacks = true;
                SupportsModrinth = true;
                SupportsCurseForge = true;
                PrimaryAddonSubDir = "mods";
                LoaderName = "fabric";
            }
            else if (ServerType.StartsWith("Forge", StringComparison.OrdinalIgnoreCase))
            {
                Family = EngineFamily.Forge;
                SupportsMods = true;
                SupportsModpacks = true;
                SupportsModrinth = true;
                SupportsCurseForge = true;
                PrimaryAddonSubDir = "mods";
                LoaderName = "forge";
            }
            else if (ServerType.StartsWith("NeoForge", StringComparison.OrdinalIgnoreCase))
            {
                Family = EngineFamily.NeoForge;
                SupportsMods = true;
                SupportsModpacks = true;
                SupportsModrinth = true;
                SupportsCurseForge = true;
                PrimaryAddonSubDir = "mods";
                LoaderName = "neoforge";
            }
            else if (ServerType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
            {
                Family = EngineFamily.Pocketmine;
                SupportsPlugins = true;
                SupportsPoggit = true;
                PrimaryAddonSubDir = "plugins";
                LoaderName = "pocketmine";
            }
            else if (ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
            {
                Family = EngineFamily.Bedrock;
                SupportsBedrockAddons = true;
                // Bedrock Dedicated usually doesn't have a marketplace flow, 
                // but we might allow local imports to behavior_packs
                PrimaryAddonSubDir = "behavior_packs";
                LoaderName = "bedrock";
            }
            else
            {
                Family = EngineFamily.Vanilla;
                PrimaryAddonSubDir = "mods"; // Fallback
                LoaderName = "vanilla";
            }
        }

        public bool IsJavaEngine => Family == EngineFamily.Vanilla || 
                                    Family == EngineFamily.Spigot || 
                                    Family == EngineFamily.Fabric || 
                                    Family == EngineFamily.Forge || 
                                    Family == EngineFamily.NeoForge;
    }
}
