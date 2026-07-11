using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PocketMC.Domain.Models;

namespace PocketMC.Domain.Models
{

    public sealed class JavaModMetadata
    {
        public string ModId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string LoaderType { get; set; } = "Unknown"; // Fabric, Quilt, Forge, NeoForge, Plugin, Unknown
        public string? IconEntryPath { get; set; }
        public byte[]? IconBytes { get; set; }

        public string? RequiredMinecraftVersion { get; set; }
        public string? RequiredLoaderVersion { get; set; }

        public ModSideSupport SideSupport { get; set; } = ModSideSupport.Unknown;
        public string SideLabel { get; set; } = "Unknown";

        public bool IsClientOnly
        {
            get => SideSupport == ModSideSupport.ClientOnly;
            set
            {
                if (value)
                    SideSupport = ModSideSupport.ClientOnly;
                else if (SideSupport == ModSideSupport.ClientOnly)
                    SideSupport = ModSideSupport.Unknown;
            }
        }

        public bool IsPluginInModsFolder { get; set; }
        public bool HasPluginMetadata { get; set; }
        public string? ApiVersion { get; set; }
        public List<string> RequiredDependencies { get; set; } = new();
        public List<string> OptionalDependencies { get; set; } = new();
        public List<string> Dependencies => RequiredDependencies.Concat(OptionalDependencies).ToList();

        public void SanitizeDependencies()
        {
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "minecraft", "forge", "neoforge", "java", "fabric", "fabricloader", 
                "quilt_loader", "quilt-loader", "fml", "fabric-api"
            };

            if (!string.IsNullOrEmpty(ModId))
            {
                ignored.Add(ModId);
            }

            var newRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in RequiredDependencies)
            {
                if (!ignored.Contains(dep) && !IsFabricApiModule(dep))
                {
                    newRequired.Add(dep);
                }
            }
            RequiredDependencies = newRequired.ToList();

            var newOptional = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in OptionalDependencies)
            {
                if (!ignored.Contains(dep) && !newRequired.Contains(dep) && !IsFabricApiModule(dep))
                {
                    newOptional.Add(dep);
                }
            }
            OptionalDependencies = newOptional.ToList();
        }

        private static bool IsFabricApiModule(string dependencyId)
        {
            // Internal Fabric API submodules usually follow the pattern "fabric-something-vX"
            // For example: fabric-rendering-fluids-v1, fabric-block-getter-api-v2
            return Regex.IsMatch(dependencyId, @"^fabric-[a-z0-9-]+-v\d+$", RegexOptions.IgnoreCase);
        }
    }
}
