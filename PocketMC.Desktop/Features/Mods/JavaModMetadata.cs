using System;
using System.Collections.Generic;
using System.Linq;
namespace PocketMC.Desktop.Features.Mods
{
    public enum ModSideSupport
    {
        Unknown,
        ClientOnly,
        ServerOnly,
        ClientAndServer,
        OptionalOnServer,
        OptionalOnClient
    }

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
        public List<string> Warnings { get; set; } = new();
    }
}
