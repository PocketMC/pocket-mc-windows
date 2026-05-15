using System;
using System.Collections.Generic;

namespace PocketMC.Desktop.Models
{
    public sealed class ServerConfiguration
    {
        public int MinRamMb { get; set; } = 1024;
        public int MaxRamMb { get; set; } = 4096;
        public string? CustomJavaPath { get; set; }
        public string? AdvancedJvmArgs { get; set; }
        public bool EnableAutoRestart { get; set; }
        public int MaxAutoRestarts { get; set; } = 3;
        public int AutoRestartDelaySeconds { get; set; } = 10;
        public int BackupIntervalHours { get; set; } = 0;
        public int MaxBackupsToKeep { get; set; } = 10;

        public string Motd { get; set; } = "A Minecraft Server";
        public string Seed { get; set; } = "";
        public string SpawnProtection { get; set; } = "16";
        public string MaxPlayers { get; set; } = "20";
        public string ServerPort { get; set; } = "25565";
        public string ServerPortV6 { get; set; } = "19133";
        public string ServerIp { get; set; } = "";
        public string LevelType { get; set; } = "minecraft:normal";
        public bool OnlineMode { get; set; }
        public bool Pvp { get; set; } = true;
        public bool WhiteList { get; set; }
        public string Gamemode { get; set; } = "survival";
        public string Difficulty { get; set; } = "easy";
        public bool AllowCommandBlock { get; set; }
        public bool AllowFlight { get; set; }
        public bool AllowNether { get; set; } = true;
        public bool AllowCheats { get; set; }
        public bool TexturepackRequired { get; set; }
        public bool ForceGamemode { get; set; }
        public string DefaultPlayerPermissionLevel { get; set; } = "member";
        public string TickDistance { get; set; } = "4";
        public string ViewDistance { get; set; } = "10";
        public string SimulationDistance { get; set; } = "10";

        public Dictionary<string, string> AdvancedProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AllProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
