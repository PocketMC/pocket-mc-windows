using System;

namespace PocketMC.Domain.Models
{
    public enum BedrockPackType
    {
        Behavior,
        Resource
    }

    public sealed class BedrockPackInfo
    {
        public string Uuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public string MinEngineVersion { get; set; } = string.Empty;
        public BedrockPackType PackType { get; set; } = BedrockPackType.Behavior;
        public string DirectoryPath { get; set; } = string.Empty;
        public string? IconPath { get; set; }
        public bool IsEnabled { get; set; }
        public int LoadOrder { get; set; } = -1;
        public double SizeKb { get; set; }
        public DateTime LastModified { get; set; }
    }
}
