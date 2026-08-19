using System;
using System.Collections.Generic;

namespace PocketMC.Domain.Models
{
    public class SettingsBackupCategories
    {
        public bool IncludeAppBehavior { get; set; } = true;
        public bool IncludeAppearance { get; set; } = true;
        public bool IncludeStoragePaths { get; set; } = true;
        public bool IncludeNotifications { get; set; } = true;
        public bool IncludeAiConfiguration { get; set; } = true;
        public bool IncludeAiApiKeys { get; set; } = true;
        public bool IncludeCurseForgeApiKey { get; set; } = true;
        public bool IncludeDiscord { get; set; } = true;
        public bool IncludePlayitTunnel { get; set; } = true;
        public bool IncludeCloudBackups { get; set; } = true;
        public bool IncludeRemoteControl { get; set; } = true;
    }

    public class AppBehaviorBackupData
    {
        public bool StartWithWindows { get; set; }
        public bool StartMinimizedToTray { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool KeepComputerAwakeWhileServersRunning { get; set; } = true;
        public int ConsoleBufferSize { get; set; } = 5000;
        public bool EnableTelemetry { get; set; } = true;
        public string? LastSeenChangelogVersion { get; set; }
        public bool HasCompletedFirstLaunch { get; set; }
        public HashSet<int>? UserRemovedJavaVersions { get; set; }
    }

    public class AppearanceBackupData
    {
        public string WindowBackdrop { get; set; } = "FakeMica";
        public string AccentColorMode { get; set; } = "Custom";
        public string? CustomAccentColor { get; set; } = "#008B00";
        public string? CustomBackgroundImagePath { get; set; }
        public bool HasMigratedToGreenWallpaperBlurTheme { get; set; }
        public bool HasMigratedToDefaultImageWallpaper { get; set; }
    }

    public class StoragePathsBackupData
    {
        public string? AppRootPath { get; set; }
        public string? PlayitConfigDirectory { get; set; }
        public string? ExternalBackupDirectory { get; set; }
    }

    public class NotificationsBackupData
    {
        public bool EnableServerOnlineNotifications { get; set; } = true;
        public bool EnableAgentConnectNotifications { get; set; } = true;
        public bool EnableRemoteControlNotifications { get; set; } = true;
        public bool EnableAiSummaryNotifications { get; set; } = true;
        public bool EnableDiscordNotifications { get; set; } = true;
    }

    public class AiBackupData
    {
        public string AiProvider { get; set; } = "Gemini";
        public bool EnableAiSummarization { get; set; }
        public bool AlwaysAutoSummarize { get; set; }
        public Dictionary<string, string> AiModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AiEndpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AiApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class CurseForgeBackupData
    {
        public string? CurseForgeApiKey { get; set; }
    }

    public class DiscordBackupData
    {
        public bool EnableDiscordRpc { get; set; } = true;
        public string? DiscordUserId { get; set; }
        public string? DiscordApiUrl { get; set; }
        public string? DiscordApiKey { get; set; }
    }

    public class CloudBackupsBackupData
    {
        public CloudBackupSettings CloudBackups { get; set; } = new();
        public Dictionary<string, CloudOAuthTokenSet> CloudTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class SettingsBackupPackage
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string? AppVersion { get; set; }
        public SettingsBackupCategories IncludedCategories { get; set; } = new();

        public AppBehaviorBackupData? AppBehavior { get; set; }
        public AppearanceBackupData? Appearance { get; set; }
        public StoragePathsBackupData? StoragePaths { get; set; }
        public NotificationsBackupData? Notifications { get; set; }
        public AiBackupData? AiConfiguration { get; set; }
        public CurseForgeBackupData? CurseForge { get; set; }
        public DiscordBackupData? Discord { get; set; }
        public PlayitPartnerConnection? PlayitTunnel { get; set; }
        public CloudBackupsBackupData? CloudBackups { get; set; }
        public RemoteControlSettings? RemoteControl { get; set; }
    }
}
