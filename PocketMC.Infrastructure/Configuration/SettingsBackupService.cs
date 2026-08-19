using System;
using System.Collections.Generic;
using System.Text.Json;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Configuration
{
    public class SettingsBackupService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public SettingsBackupPackage CreateBackupPackage(
            AppSettings settings,
            SettingsBackupCategories categories,
            string? appVersion = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (categories == null) throw new ArgumentNullException(nameof(categories));

            var package = new SettingsBackupPackage
            {
                Version = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                AppVersion = string.IsNullOrWhiteSpace(appVersion) ? AppConfig.AppVersion : appVersion,
                IncludedCategories = categories
            };

            if (categories.IncludeAppBehavior)
            {
                package.AppBehavior = new AppBehaviorBackupData
                {
                    StartWithWindows = settings.StartWithWindows,
                    StartMinimizedToTray = settings.StartMinimizedToTray,
                    MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose,
                    KeepComputerAwakeWhileServersRunning = settings.KeepComputerAwakeWhileServersRunning,
                    ConsoleBufferSize = settings.ConsoleBufferSize,
                    EnableTelemetry = settings.EnableTelemetry,
                    LastSeenChangelogVersion = settings.LastSeenChangelogVersion,
                    HasCompletedFirstLaunch = settings.HasCompletedFirstLaunch,
                    UserRemovedJavaVersions = settings.UserRemovedJavaVersions != null ? new HashSet<int>(settings.UserRemovedJavaVersions) : null
                };
            }

            if (categories.IncludeAppearance)
            {
                package.Appearance = new AppearanceBackupData
                {
                    WindowBackdrop = settings.WindowBackdrop,
                    AccentColorMode = settings.AccentColorMode,
                    CustomAccentColor = settings.CustomAccentColor,
                    CustomBackgroundImagePath = settings.CustomBackgroundImagePath,
                    HasMigratedToGreenWallpaperBlurTheme = settings.HasMigratedToGreenWallpaperBlurTheme,
                    HasMigratedToDefaultImageWallpaper = settings.HasMigratedToDefaultImageWallpaper
                };
            }

            if (categories.IncludeStoragePaths)
            {
                package.StoragePaths = new StoragePathsBackupData
                {
                    AppRootPath = settings.AppRootPath,
                    PlayitConfigDirectory = settings.PlayitConfigDirectory,
                    ExternalBackupDirectory = settings.ExternalBackupDirectory
                };
            }

            if (categories.IncludeNotifications)
            {
                package.Notifications = new NotificationsBackupData
                {
                    EnableServerOnlineNotifications = settings.EnableServerOnlineNotifications,
                    EnableAgentConnectNotifications = settings.EnableAgentConnectNotifications,
                    EnableRemoteControlNotifications = settings.EnableRemoteControlNotifications,
                    EnableAiSummaryNotifications = settings.EnableAiSummaryNotifications,
                    EnableDiscordNotifications = settings.EnableDiscordNotifications
                };
            }

            if (categories.IncludeAiConfiguration || categories.IncludeAiApiKeys)
            {
                var aiData = new AiBackupData();
                if (categories.IncludeAiConfiguration)
                {
                    aiData.AiProvider = settings.AiProvider;
                    aiData.EnableAiSummarization = settings.EnableAiSummarization;
                    aiData.AlwaysAutoSummarize = settings.AlwaysAutoSummarize;
                    if (settings.AiModels != null)
                    {
                        foreach (var kvp in settings.AiModels)
                        {
                            aiData.AiModels[kvp.Key] = kvp.Value;
                        }
                    }
                    if (settings.AiEndpoints != null)
                    {
                        foreach (var kvp in settings.AiEndpoints)
                        {
                            aiData.AiEndpoints[kvp.Key] = kvp.Value;
                        }
                    }
                }

                if (categories.IncludeAiApiKeys && settings.AiApiKeys != null)
                {
                    foreach (var kvp in settings.AiApiKeys)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            aiData.AiApiKeys[kvp.Key] = kvp.Value;
                        }
                    }
                }

                package.AiConfiguration = aiData;
            }

            if (categories.IncludeCurseForgeApiKey)
            {
                package.CurseForge = new CurseForgeBackupData
                {
                    CurseForgeApiKey = settings.CurseForgeApiKey
                };
            }

            if (categories.IncludeDiscord)
            {
                package.Discord = new DiscordBackupData
                {
                    EnableDiscordRpc = settings.EnableDiscordRpc,
                    DiscordUserId = settings.DiscordUserId,
                    DiscordApiUrl = settings.DiscordApiUrl,
                    DiscordApiKey = settings.DiscordApiKey
                };
            }

            if (categories.IncludePlayitTunnel && settings.PlayitPartnerConnection != null)
            {
                package.PlayitTunnel = new PlayitPartnerConnection
                {
                    AgentId = settings.PlayitPartnerConnection.AgentId,
                    AgentSecretKey = settings.PlayitPartnerConnection.AgentSecretKey,
                    AccountId = settings.PlayitPartnerConnection.AccountId,
                    ConnectedEmail = settings.PlayitPartnerConnection.ConnectedEmail,
                    Platform = settings.PlayitPartnerConnection.Platform,
                    AgentVersion = settings.PlayitPartnerConnection.AgentVersion,
                    ConnectedAtUtc = settings.PlayitPartnerConnection.ConnectedAtUtc
                };
            }

            if (categories.IncludeCloudBackups)
            {
                var cloudData = new CloudBackupsBackupData
                {
                    CloudBackups = CloneObject(settings.CloudBackups) ?? new CloudBackupSettings()
                };

                if (settings.CloudTokens != null)
                {
                    foreach (var kvp in settings.CloudTokens)
                    {
                        if (kvp.Value != null)
                        {
                            cloudData.CloudTokens[kvp.Key] = CloneObject(kvp.Value)!;
                        }
                    }
                }

                package.CloudBackups = cloudData;
            }

            if (categories.IncludeRemoteControl && settings.RemoteControl != null)
            {
                package.RemoteControl = CloneObject(settings.RemoteControl);
            }

            return package;
        }

        public string ExportToJson(
            AppSettings settings,
            SettingsBackupCategories categories,
            string? appVersion = null)
        {
            var package = CreateBackupPackage(settings, categories, appVersion);
            return JsonSerializer.Serialize(package, JsonOptions);
        }

        public SettingsBackupPackage DeserializePackage(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                throw new ArgumentException("Backup content cannot be empty.", nameof(jsonContent));
            }

            var package = JsonSerializer.Deserialize<SettingsBackupPackage>(jsonContent, JsonOptions);
            if (package == null)
            {
                throw new JsonException("Failed to deserialize settings backup package.");
            }

            return package;
        }

        public SettingsBackupCategories GetAvailableCategories(SettingsBackupPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            return new SettingsBackupCategories
            {
                IncludeAppBehavior = package.AppBehavior != null,
                IncludeAppearance = package.Appearance != null,
                IncludeStoragePaths = package.StoragePaths != null,
                IncludeNotifications = package.Notifications != null,
                IncludeAiConfiguration = package.AiConfiguration != null,
                IncludeAiApiKeys = package.AiConfiguration?.AiApiKeys != null && package.AiConfiguration.AiApiKeys.Count > 0,
                IncludeCurseForgeApiKey = package.CurseForge != null && !string.IsNullOrWhiteSpace(package.CurseForge.CurseForgeApiKey),
                IncludeDiscord = package.Discord != null,
                IncludePlayitTunnel = package.PlayitTunnel != null,
                IncludeCloudBackups = package.CloudBackups != null,
                IncludeRemoteControl = package.RemoteControl != null
            };
        }

        public AppSettings RestoreFromPackage(
            AppSettings targetSettings,
            SettingsBackupPackage package,
            SettingsBackupCategories categoriesToRestore)
        {
            if (targetSettings == null) throw new ArgumentNullException(nameof(targetSettings));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (categoriesToRestore == null) throw new ArgumentNullException(nameof(categoriesToRestore));

            // App Behavior
            if (categoriesToRestore.IncludeAppBehavior && package.AppBehavior != null)
            {
                targetSettings.StartWithWindows = package.AppBehavior.StartWithWindows;
                targetSettings.StartMinimizedToTray = package.AppBehavior.StartMinimizedToTray;
                targetSettings.MinimizeToTrayOnClose = package.AppBehavior.MinimizeToTrayOnClose;
                targetSettings.KeepComputerAwakeWhileServersRunning = package.AppBehavior.KeepComputerAwakeWhileServersRunning;
                targetSettings.ConsoleBufferSize = package.AppBehavior.ConsoleBufferSize;
                targetSettings.EnableTelemetry = package.AppBehavior.EnableTelemetry;
                targetSettings.LastSeenChangelogVersion = package.AppBehavior.LastSeenChangelogVersion;
                targetSettings.HasCompletedFirstLaunch = package.AppBehavior.HasCompletedFirstLaunch;
                if (package.AppBehavior.UserRemovedJavaVersions != null)
                {
                    targetSettings.UserRemovedJavaVersions = new HashSet<int>(package.AppBehavior.UserRemovedJavaVersions);
                }
            }

            // Appearance
            if (categoriesToRestore.IncludeAppearance && package.Appearance != null)
            {
                targetSettings.WindowBackdrop = package.Appearance.WindowBackdrop;
                targetSettings.AccentColorMode = package.Appearance.AccentColorMode;
                targetSettings.CustomAccentColor = package.Appearance.CustomAccentColor;
                targetSettings.CustomBackgroundImagePath = package.Appearance.CustomBackgroundImagePath;
                targetSettings.HasMigratedToGreenWallpaperBlurTheme = package.Appearance.HasMigratedToGreenWallpaperBlurTheme;
                targetSettings.HasMigratedToDefaultImageWallpaper = package.Appearance.HasMigratedToDefaultImageWallpaper;
            }

            // Storage Paths
            if (categoriesToRestore.IncludeStoragePaths && package.StoragePaths != null)
            {
                if (!string.IsNullOrWhiteSpace(package.StoragePaths.AppRootPath))
                {
                    targetSettings.AppRootPath = package.StoragePaths.AppRootPath;
                }
                if (!string.IsNullOrWhiteSpace(package.StoragePaths.PlayitConfigDirectory))
                {
                    targetSettings.PlayitConfigDirectory = package.StoragePaths.PlayitConfigDirectory;
                }
                if (package.StoragePaths.ExternalBackupDirectory != null)
                {
                    targetSettings.ExternalBackupDirectory = package.StoragePaths.ExternalBackupDirectory;
                }
            }

            // Notifications
            if (categoriesToRestore.IncludeNotifications && package.Notifications != null)
            {
                targetSettings.EnableServerOnlineNotifications = package.Notifications.EnableServerOnlineNotifications;
                targetSettings.EnableAgentConnectNotifications = package.Notifications.EnableAgentConnectNotifications;
                targetSettings.EnableRemoteControlNotifications = package.Notifications.EnableRemoteControlNotifications;
                targetSettings.EnableAiSummaryNotifications = package.Notifications.EnableAiSummaryNotifications;
                targetSettings.EnableDiscordNotifications = package.Notifications.EnableDiscordNotifications;
            }

            // AI Configuration
            if (package.AiConfiguration != null)
            {
                if (categoriesToRestore.IncludeAiConfiguration)
                {
                    targetSettings.AiProvider = package.AiConfiguration.AiProvider;
                    targetSettings.EnableAiSummarization = package.AiConfiguration.EnableAiSummarization;
                    targetSettings.AlwaysAutoSummarize = package.AiConfiguration.AlwaysAutoSummarize;

                    targetSettings.AiModels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in package.AiConfiguration.AiModels)
                    {
                        targetSettings.AiModels[kvp.Key] = kvp.Value;
                    }

                    targetSettings.AiEndpoints ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in package.AiConfiguration.AiEndpoints)
                    {
                        targetSettings.AiEndpoints[kvp.Key] = kvp.Value;
                    }
                }

                if (categoriesToRestore.IncludeAiApiKeys && package.AiConfiguration.AiApiKeys != null)
                {
                    targetSettings.AiApiKeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in package.AiConfiguration.AiApiKeys)
                    {
                        targetSettings.AiApiKeys[kvp.Key] = kvp.Value;
                    }
                }
            }

            // CurseForge
            if (categoriesToRestore.IncludeCurseForgeApiKey && package.CurseForge != null)
            {
                targetSettings.CurseForgeApiKey = package.CurseForge.CurseForgeApiKey;
            }

            // Discord
            if (categoriesToRestore.IncludeDiscord && package.Discord != null)
            {
                targetSettings.EnableDiscordRpc = package.Discord.EnableDiscordRpc;
                targetSettings.DiscordUserId = package.Discord.DiscordUserId;
                targetSettings.DiscordApiUrl = package.Discord.DiscordApiUrl;
                targetSettings.DiscordApiKey = package.Discord.DiscordApiKey;
            }

            // Playit Tunnel
            if (categoriesToRestore.IncludePlayitTunnel && package.PlayitTunnel != null)
            {
                targetSettings.PlayitPartnerConnection = new PlayitPartnerConnection
                {
                    AgentId = package.PlayitTunnel.AgentId,
                    AgentSecretKey = package.PlayitTunnel.AgentSecretKey,
                    AccountId = package.PlayitTunnel.AccountId,
                    ConnectedEmail = package.PlayitTunnel.ConnectedEmail,
                    Platform = package.PlayitTunnel.Platform,
                    AgentVersion = package.PlayitTunnel.AgentVersion,
                    ConnectedAtUtc = package.PlayitTunnel.ConnectedAtUtc
                };
            }

            // Cloud Backups
            if (categoriesToRestore.IncludeCloudBackups && package.CloudBackups != null)
            {
                if (package.CloudBackups.CloudBackups != null)
                {
                    targetSettings.CloudBackups = CloneObject(package.CloudBackups.CloudBackups) ?? new CloudBackupSettings();
                }

                if (package.CloudBackups.CloudTokens != null)
                {
                    targetSettings.CloudTokens ??= new Dictionary<string, CloudOAuthTokenSet>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in package.CloudBackups.CloudTokens)
                    {
                        if (kvp.Value != null)
                        {
                            targetSettings.CloudTokens[kvp.Key] = CloneObject(kvp.Value)!;
                        }
                    }
                }
            }

            // Remote Control
            if (categoriesToRestore.IncludeRemoteControl && package.RemoteControl != null)
            {
                targetSettings.RemoteControl = CloneObject(package.RemoteControl) ?? new RemoteControlSettings();
            }

            return targetSettings;
        }

        private static T? CloneObject<T>(T? obj) where T : class
        {
            if (obj == null) return null;
            string json = JsonSerializer.Serialize(obj, JsonOptions);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
    }
}
