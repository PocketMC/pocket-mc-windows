using System;


namespace PocketMC.Domain.Models
{
    public class PlayitPartnerConnection
    {
        public string? AgentId { get; set; }
        public string? AgentSecretKey { get; set; }
        public long? AccountId { get; set; }
        public string? ConnectedEmail { get; set; }
        public string? Platform { get; set; }
        public string? AgentVersion { get; set; }
        public DateTimeOffset? ConnectedAtUtc { get; set; }
    }

    public class AppSettings
    {
        public string? AppRootPath { get; set; }
        public string? PlayitConfigDirectory { get; set; }
        public PlayitPartnerConnection? PlayitPartnerConnection { get; set; }
        public bool HasCompletedFirstLaunch { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartOnSystemBoot { get; set; }
        public bool StartMinimizedToTray { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool KeepComputerAwakeWhileServersRunning { get; set; } = true;
        public string WindowBackdrop { get; set; } = "FakeMica";
        public string AccentColorMode { get; set; } = "Custom";
        public string? CustomAccentColor { get; set; } = "#008B00";
        public bool HasMigratedToGreenWallpaperBlurTheme { get; set; }
        public bool HasMigratedToDefaultImageWallpaper { get; set; }
        public string? CustomBackgroundImagePath { get; set; }
        public string? CurseForgeApiKey { get; set; }

        // Telemetry
        public bool EnableTelemetry { get; set; } = true;
        public Guid? TelemetryClientId { get; set; }
        public bool HasReportedInstall { get; set; }

        // AI Summarization
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? AiApiKey { get; set; }
        public System.Collections.Generic.Dictionary<string, string> AiApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public System.Collections.Generic.Dictionary<string, string> AiModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public System.Collections.Generic.Dictionary<string, string> AiEndpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool EnableAiSummarization { get; set; } = false;
        public string AiProvider { get; set; } = "Gemini";
        public bool AlwaysAutoSummarize { get; set; } = false;

        public string? GetCurrentAiKey() => AiApiKeys.TryGetValue(AiProvider, out var key) ? key : null;
        public string? GetCurrentAiModel() => AiModels.TryGetValue(AiProvider, out var model) ? model : null;
        public string? GetCurrentAiEndpoint() => AiEndpoints.TryGetValue(AiProvider, out var ep) ? ep : null;

        // Disaster Recovery
        public string? ExternalBackupDirectory { get; set; }

        // Console Settings
        public int ConsoleBufferSize { get; set; } = 5000;

        // Discord Rich Presence
        public bool EnableDiscordRpc { get; set; } = true;

        // User Intent Flags
        public System.Collections.Generic.HashSet<int> UserRemovedJavaVersions { get; set; } = new();

        // Cloud Backups
        public CloudBackupSettings CloudBackups { get; set; } = new();
        public System.Collections.Generic.Dictionary<string, CloudOAuthTokenSet> CloudTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Remote Control
        public RemoteControlSettings RemoteControl { get; set; } = new();

        // Discord Bot Integration
        public string? DiscordUserId { get; set; }
        public string? DiscordApiUrl { get; set; }
        public string? DiscordApiKey { get; set; }
        public bool EnableDiscordNotifications { get; set; } = true;
        public bool EnableServerOnlineNotifications { get; set; } = true;
        public bool EnableAgentConnectNotifications { get; set; } = true;
        public bool EnableRemoteControlNotifications { get; set; } = true;
        public bool EnableAiSummaryNotifications { get; set; } = true;

        // What's New
        public string? LastSeenChangelogVersion { get; set; }
    }
}
