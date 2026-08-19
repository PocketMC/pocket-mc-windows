using System;
using System.IO;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Configuration;

public sealed class SettingsBackupServiceTests
{
    private readonly SettingsBackupService _service = new();

    [Fact]
    public void ExportPackage_OnlyIncludesSelectedCategories()
    {
        var settings = new AppSettings
        {
            AppRootPath = @"D:\PocketMC\Data",
            PlayitConfigDirectory = @"C:\Users\test\.playit",
            ExternalBackupDirectory = @"E:\Backups",
            WindowBackdrop = "Mica",
            AccentColorMode = "Custom",
            CustomAccentColor = "#FF0000",
            CurseForgeApiKey = "cf-api-key-12345",
            StartWithWindows = true,
            EnableServerOnlineNotifications = false,
            EnableAiSummarization = true,
            AiProvider = "Claude"
        };
        settings.AiApiKeys["Claude"] = "claude-secret-key";

        // User only selects Appearance and AI Settings (without AI API keys)
        var categories = new SettingsBackupCategories
        {
            IncludeAppBehavior = false,
            IncludeAppearance = true,
            IncludeStoragePaths = false,
            IncludeNotifications = false,
            IncludeAiConfiguration = true,
            IncludeAiApiKeys = false,
            IncludeCurseForgeApiKey = false,
            IncludeDiscord = false,
            IncludePlayitTunnel = false,
            IncludeCloudBackups = false,
            IncludeRemoteControl = false
        };

        var package = _service.CreateBackupPackage(settings, categories, "1.4.0");

        Assert.Null(package.AppBehavior);
        Assert.Null(package.StoragePaths);
        Assert.Null(package.Notifications);
        Assert.Null(package.CurseForge);
        Assert.Null(package.Discord);
        Assert.Null(package.PlayitTunnel);
        Assert.Null(package.CloudBackups);
        Assert.Null(package.RemoteControl);

        Assert.NotNull(package.Appearance);
        Assert.Equal("Mica", package.Appearance.WindowBackdrop);
        Assert.Equal("#FF0000", package.Appearance.CustomAccentColor);

        Assert.NotNull(package.AiConfiguration);
        Assert.Equal("Claude", package.AiConfiguration.AiProvider);
        Assert.True(package.AiConfiguration.EnableAiSummarization);
        Assert.Empty(package.AiConfiguration.AiApiKeys);
    }

    [Fact]
    public void ExportPackage_IncludesKeysAndPathsWhenSelectedByUser()
    {
        var settings = new AppSettings
        {
            AppRootPath = @"D:\CustomPath\PocketMC",
            CurseForgeApiKey = "my-secret-curseforge-key",
            DiscordApiKey = "my-discord-bot-key"
        };
        settings.AiApiKeys["Gemini"] = "gemini-test-key";

        var categories = new SettingsBackupCategories
        {
            IncludeStoragePaths = true,
            IncludeCurseForgeApiKey = true,
            IncludeAiConfiguration = true,
            IncludeAiApiKeys = true,
            IncludeDiscord = true
        };

        var package = _service.CreateBackupPackage(settings, categories);

        Assert.NotNull(package.StoragePaths);
        Assert.Equal(@"D:\CustomPath\PocketMC", package.StoragePaths.AppRootPath);

        Assert.NotNull(package.CurseForge);
        Assert.Equal("my-secret-curseforge-key", package.CurseForge.CurseForgeApiKey);

        Assert.NotNull(package.AiConfiguration);
        Assert.Equal("gemini-test-key", package.AiConfiguration.AiApiKeys["Gemini"]);

        Assert.NotNull(package.Discord);
        Assert.Equal("my-discord-bot-key", package.Discord.DiscordApiKey);
    }

    [Fact]
    public void RestoreFromPackage_SelectivelyMergesWithoutWipingUnselectedFields()
    {
        var currentSettings = new AppSettings
        {
            AppRootPath = @"D:\Current\PocketMC",
            CurseForgeApiKey = "existing-cf-key",
            WindowBackdrop = "Dark",
            AccentColorMode = "Automatic",
            StartWithWindows = false
        };
        currentSettings.AiApiKeys["Gemini"] = "existing-gemini-key";

        var backupPackage = new SettingsBackupPackage
        {
            AppBehavior = new AppBehaviorBackupData { StartWithWindows = true },
            Appearance = new AppearanceBackupData { WindowBackdrop = "Mica", AccentColorMode = "Custom", CustomAccentColor = "#123456" },
            StoragePaths = new StoragePathsBackupData { AppRootPath = @"E:\NewRoot\PocketMC" },
            CurseForge = new CurseForgeBackupData { CurseForgeApiKey = "backup-cf-key" }
        };

        // User ONLY wants to restore Appearance and AppBehavior
        var restoreCategories = new SettingsBackupCategories
        {
            IncludeAppBehavior = true,
            IncludeAppearance = true,
            IncludeStoragePaths = false, // Must NOT overwrite AppRootPath!
            IncludeCurseForgeApiKey = false, // Must NOT overwrite CurseForgeApiKey!
            IncludeAiConfiguration = false,
            IncludeAiApiKeys = false,
            IncludeNotifications = false,
            IncludeDiscord = false,
            IncludePlayitTunnel = false,
            IncludeCloudBackups = false,
            IncludeRemoteControl = false
        };

        var result = _service.RestoreFromPackage(currentSettings, backupPackage, restoreCategories);

        // Updated
        Assert.True(result.StartWithWindows);
        Assert.Equal("Mica", result.WindowBackdrop);
        Assert.Equal("Custom", result.AccentColorMode);
        Assert.Equal("#123456", result.CustomAccentColor);

        // Untouched and preserved
        Assert.Equal(@"D:\Current\PocketMC", result.AppRootPath);
        Assert.Equal("existing-cf-key", result.CurseForgeApiKey);
        Assert.Equal("existing-gemini-key", result.AiApiKeys["Gemini"]);
    }

    [Fact]
    public void RoundTrip_JsonSerializationAndDeserialization()
    {
        var settings = new AppSettings
        {
            AppRootPath = @"C:\PocketMC",
            WindowBackdrop = "FakeMica",
            CustomAccentColor = "#008B00",
            CurseForgeApiKey = "secret-cf",
            EnableDiscordRpc = true
        };

        var categories = new SettingsBackupCategories();
        string json = _service.ExportToJson(settings, categories, "1.0.0");

        var deserialized = _service.DeserializePackage(json);
        Assert.Equal(1, deserialized.Version);
        Assert.Equal("1.0.0", deserialized.AppVersion);
        Assert.NotNull(deserialized.Appearance);
        Assert.Equal("#008B00", deserialized.Appearance.CustomAccentColor);
        Assert.NotNull(deserialized.StoragePaths);
        Assert.Equal(@"C:\PocketMC", deserialized.StoragePaths.AppRootPath);
    }

    [Fact]
    public void ExportPackage_DefaultsAppVersionFromAppConfigWhenOmitted()
    {
        var settings = new AppSettings();
        var categories = new SettingsBackupCategories();

        var package = _service.CreateBackupPackage(settings, categories);

        Assert.NotNull(package.AppVersion);
        Assert.Equal(AppConfig.AppVersion, package.AppVersion);
    }
}
