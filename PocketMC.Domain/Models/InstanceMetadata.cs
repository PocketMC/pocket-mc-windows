using System;

namespace PocketMC.Domain.Models
{
    public class InstanceMetadata
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ServerType { get; set; } = "Vanilla";
        public string MinecraftVersion { get; set; } = "1.20.4";
        public string LoaderVersion { get; set; } = string.Empty;
        public string Motd { get; set; } = "A Minecraft Server";
        public int MaxPlayers { get; set; } = 20;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastPlayedAt { get; set; }
        public DateTime? PinnedAt { get; set; }
        public int MinRamMb { get; set; } = 1024;
        public int MaxRamMb { get; set; } = 4096;

        // Backup configuration
        // Backup configuration
        public int BackupIntervalHours { get; set; } = 0; // 0 = manual only
        public int MaxBackupsToKeep { get; set; } = 10;
        public DateTime? LastBackupTime { get; set; }

        // Auto-Restart configuration
        public bool EnableAutoRestart { get; set; } = false;
        public int MaxAutoRestarts { get; set; } = 3;
        public int AutoRestartDelaySeconds { get; set; } = 10;

        // Runtime configuration (NET-14)
        public string? CustomJavaPath { get; set; } = null;
        public string? AdvancedJvmArgs { get; set; } = null;

        // Cross-play / Bedrock support
        /// <summary>True when Geyser + Floodgate were installed for this Java instance.</summary>
        public bool HasGeyser { get; set; } = false;
        public int? GeyserBedrockPort { get; set; } = 19132;
        public int? ServerPort { get; set; }

        // Simple Voice Chat tunnel state
        public bool SimpleVoiceChatDetected { get; set; }
        public int? SimpleVoiceChatPort { get; set; }
        public string? SimpleVoiceChatTunnelId { get; set; }
        public string? SimpleVoiceChatTunnelAddress { get; set; }
        public string? SimpleVoiceChatNumericTunnelAddress { get; set; }
        public string? SimpleVoiceChatConfigPath { get; set; }
        public string? SimpleVoiceChatVoiceHost { get; set; }
        public bool SimpleVoiceChatPromptDismissed { get; set; }
        public string? SimpleVoiceChatLastWarning { get; set; }
        public string? SimpleVoiceChatStatus { get; set; }

        // Startup behavior
        public bool AutoStartWithApp { get; set; } = false;

        /// <summary>When true, automatically check and update marketplace addons when the server starts.</summary>
        public bool AutoUpdateAddons { get; set; } = false;

        // Custom backup destination (added for local backup routing)
        public string? CustomBackupDirectory { get; set; } = null;

        [System.Text.Json.Serialization.JsonIgnore]
        public EngineCompatibility Compatibility => new EngineCompatibility(ServerType);
    }
}
