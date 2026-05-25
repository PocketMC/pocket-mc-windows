using System;
using System.Collections.Generic;

namespace PocketMC.Desktop.Features.Mods
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
        public bool IsClientOnly { get; set; }
        public bool IsPluginInModsFolder { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
