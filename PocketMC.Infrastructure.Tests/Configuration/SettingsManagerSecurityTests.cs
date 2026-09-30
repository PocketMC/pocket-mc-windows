using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Backups;
using PocketMC.Domain.Models;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PocketMC.Infrastructure.Tests.Configuration;

public sealed class SettingsManagerSecurityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_EncryptsCloudOAuthTokensBeforeSerializingSettings()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var manager = new SettingsManager(settingsPath);
        var settings = new AppSettings();
        settings.CloudTokens["GoogleDrive"] = new CloudOAuthTokenSet
        {
            Provider = CloudBackupProviderType.GoogleDrive,
            AccessToken = "access-token-plain",
            RefreshToken = "refresh-token-plain"
        };

        manager.Save(settings);

        string persisted = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("access-token-plain", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token-plain", persisted, StringComparison.Ordinal);
        Assert.Contains("dpapi:v2:", persisted, StringComparison.Ordinal);

        AppSettings loaded = manager.Load();
        Assert.Equal("access-token-plain", loaded.CloudTokens["GoogleDrive"].AccessToken);
        Assert.Equal("refresh-token-plain", loaded.CloudTokens["GoogleDrive"].RefreshToken);
    }

    [Fact]
    public void Load_MigratesUnprefixedDpapiSecretWithoutChangingItsPlaintext()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        string versionedPayload = DataProtector.Protect("legacy-unprefixed-secret");
        string unprefixedPayload = versionedPayload["dpapi:v2:".Length..];
        var settings = new AppSettings { CurseForgeApiKey = unprefixedPayload };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));

        var manager = new SettingsManager(settingsPath);
        AppSettings loaded = manager.Load();

        Assert.Equal("legacy-unprefixed-secret", loaded.CurseForgeApiKey);
        JsonObject persisted = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.StartsWith("dpapi:v2:", persisted["marketplace"]!["curseForgeApiKey"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ClearsOnlyCorruptedProtectedSecretAndPreservesOtherSettings()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        string protectedPayload = CreateCorruptedProtectedPayload();
        var settings = new AppSettings
        {
            AppRootPath = @"D:\PocketMC\Instances",
            CurseForgeApiKey = protectedPayload,
            WindowBackdrop = "Mica",
            EnableAiSummarization = true
        };
        settings.AiApiKeys["Gemini"] = "plain-gemini-key";
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.Null(loaded.CurseForgeApiKey);
        Assert.Equal(@"D:\PocketMC\Instances", loaded.AppRootPath);
        Assert.Equal("Mica", loaded.WindowBackdrop);
        Assert.True(loaded.EnableAiSummarization);
        Assert.Equal("plain-gemini-key", loaded.AiApiKeys["Gemini"]);
        string[] recoveryBackups = Directory.GetFiles(_tempDirectory, "settings.json.secret-recovery.*.bak");
        Assert.Single(recoveryBackups);
        JsonObject recoveryDocument = JsonNode.Parse(File.ReadAllText(recoveryBackups[0]))!.AsObject();
        Assert.Equal(protectedPayload, recoveryDocument["marketplace"]!["curseForgeApiKey"]!.GetValue<string>());
    }

    [Fact]
    public void Load_RemovesOnlyCloudTokenProviderWhenProtectedTokenCannotDecrypt()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var settings = new AppSettings
        {
            AppRootPath = @"D:\PocketMC\Instances",
            WindowBackdrop = "Mica"
        };
        settings.CloudTokens["GoogleDrive"] = new CloudOAuthTokenSet
        {
            Provider = CloudBackupProviderType.GoogleDrive,
            AccessToken = CreateCorruptedProtectedPayload(),
            RefreshToken = "refresh-token-plain"
        };
        settings.CloudTokens["OneDrive"] = new CloudOAuthTokenSet
        {
            Provider = CloudBackupProviderType.OneDrive,
            AccessToken = "one-access-token",
            RefreshToken = "one-refresh-token"
        };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.False(loaded.CloudTokens.ContainsKey("GoogleDrive"));
        Assert.True(loaded.CloudTokens.ContainsKey("OneDrive"));
        Assert.Equal("one-access-token", loaded.CloudTokens["OneDrive"].AccessToken);
        Assert.Equal(@"D:\PocketMC\Instances", loaded.AppRootPath);
        Assert.Equal("Mica", loaded.WindowBackdrop);
    }

    [Fact]
    public void Load_RemovesNullCloudTokenEntriesFromMalformedSettings()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "CloudTokens": {
            "GoogleDrive": null
          }
        }
        """);

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.Empty(loaded.CloudTokens);
    }

    [Fact]
    public void Load_WhenJsonIsCorrupted_CreatesRescueBackupFile()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{ invalid json corrupt content !!! ");

        var manager = new SettingsManager(settingsPath);
        AppSettings loaded = manager.Load();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.SchemaVersion);

        string[] backupFiles = Directory.GetFiles(_tempDirectory, "settings.json.corrupted.*.bak");
        Assert.Single(backupFiles);
        Assert.Contains("invalid json corrupt content", File.ReadAllText(backupFiles[0]));
    }

    [Fact]
    public void Load_MigratesLegacySettingsToSchemaVersion2AndInitializesRemoteControl()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "AppRootPath": "D:\\PocketMC\\Servers",
          "HasCompletedFirstLaunch": true
        }
        """);

        var manager = new SettingsManager(settingsPath);
        AppSettings loaded = manager.Load();

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.NotNull(loaded.RemoteControl);
        Assert.Equal(25580, loaded.RemoteControl.Port);
        Assert.NotNull(loaded.RemoteControl.Users);
        Assert.False(string.IsNullOrWhiteSpace(loaded.RemoteControl.SecurityStamp));
    }

    [Fact]
    public void Load_MigratesFlatSettingsToSectionedFormatWithoutChangingValuesOrProtectedSecrets()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        string protectedApiKey = DataProtector.Protect("existing-api-key");
        var settings = new AppSettings
        {
            AppRootPath = @"D:\PocketMC\CustomInstances",
            HasCompletedFirstLaunch = true,
            HasMigratedToGreenWallpaperBlurTheme = true,
            HasMigratedToDefaultImageWallpaper = true,
            WindowBackdrop = "Mica",
            CustomAccentColor = "#123456",
            EnableAiSummarization = false,
            CurseForgeApiKey = protectedApiKey
        };
        settings.AiApiKeys["Gemini"] = protectedApiKey;
        JsonObject legacyDocument = JsonNode.Parse(JsonSerializer.Serialize(settings))!.AsObject();
        legacyDocument["FutureRootOption"] = "keep-me";
        File.WriteAllText(settingsPath, legacyDocument.ToJsonString());

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.Equal(@"D:\PocketMC\CustomInstances", loaded.AppRootPath);
        Assert.Equal("Mica", loaded.WindowBackdrop);
        Assert.Equal("#123456", loaded.CustomAccentColor);
        Assert.False(loaded.EnableAiSummarization);
        Assert.Equal("existing-api-key", loaded.CurseForgeApiKey);
        Assert.Equal("existing-api-key", loaded.AiApiKeys["Gemini"]);

        JsonObject migrated = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.Equal(3, migrated[SettingsDocumentCodec.FormatVersionProperty]!.GetValue<int>());
        Assert.Equal(@"D:\PocketMC\CustomInstances", migrated["application"]!["appRootPath"]!.GetValue<string>());
        Assert.Equal("Mica", migrated["appearance"]!["windowBackdrop"]!.GetValue<string>());
        Assert.False(migrated["ai"]!["enableAiSummarization"]!.GetValue<bool>());
        Assert.True(migrated["marketplace"]?["curseForgeApiKey"] is JsonValue, migrated.ToJsonString());
        Assert.Equal(protectedApiKey, migrated["marketplace"]!["curseForgeApiKey"]!.GetValue<string>());
        Assert.Equal(protectedApiKey, migrated["ai"]!["aiApiKeys"]!["Gemini"]!.GetValue<string>());
        Assert.Equal(@"D:\PocketMC\CustomInstances", migrated[nameof(AppSettings.AppRootPath)]!.GetValue<string>());
        Assert.Equal("keep-me", migrated["FutureRootOption"]!.GetValue<string>());
        Assert.True(File.Exists(SettingsDocumentCodec.GetSnapshotPath(settingsPath)));
    }

    [Fact]
    public void Load_VersionTwoMigrationKeepsExactPreMigrationBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        const string original = """
        {
          "formatVersion": 2,
          "SchemaVersion": 2,
          "AppRootPath": "D:\\PocketMC\\Instances"
        }
        """;
        File.WriteAllText(settingsPath, original);

        new SettingsManager(settingsPath).Load();

        string[] backups = Directory.GetFiles(_tempDirectory, "settings.json.legacy.*.bak");
        Assert.Single(backups);
        Assert.Equal(original, File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Save_VersionTwoSettingsKeepsExactPreMigrationBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        const string original = """
        {
          "formatVersion": 2,
          "SchemaVersion": 2,
          "AppRootPath": "D:\\PocketMC\\Instances"
        }
        """;
        File.WriteAllText(settingsPath, original);

        new SettingsManager(settingsPath).Save(new AppSettings
        {
            AppRootPath = @"D:\PocketMC\Instances"
        });

        string[] backups = Directory.GetFiles(_tempDirectory, "settings.json.legacy.*.bak");
        Assert.Single(backups);
        Assert.Equal(original, File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Load_PreservesLegacyAiKeyWhenGeminiKeyAlreadyExists()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var settings = new AppSettings { AiApiKey = "legacy-ai-key" };
        settings.AiApiKeys["Gemini"] = "current-gemini-key";
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));

        var manager = new SettingsManager(settingsPath);
        AppSettings loaded = manager.Load();

        Assert.Equal("legacy-ai-key", loaded.AiApiKey);
        Assert.Equal("current-gemini-key", loaded.AiApiKeys["Gemini"]);
        JsonObject persisted = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.StartsWith("dpapi:v2:", persisted["ai"]!["aiApiKey"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-ai-key", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_LegacySettingsWithUnsetAppearanceMigrationFlagsPreservesUserAppearance()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "SchemaVersion": 2,
          "HasCompletedFirstLaunch": true,
          "WindowBackdrop": "Mica",
          "AccentColorMode": "Automatic",
          "CustomAccentColor": "#13579B",
          "CustomBackgroundImagePath": null,
          "HasMigratedToGreenWallpaperBlurTheme": false,
          "HasMigratedToDefaultImageWallpaper": false
        }
        """);

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.Equal("Mica", loaded.WindowBackdrop);
        Assert.Equal("Automatic", loaded.AccentColorMode);
        Assert.Equal("#13579B", loaded.CustomAccentColor);
        Assert.Null(loaded.CustomBackgroundImagePath);
        Assert.True(loaded.HasMigratedToGreenWallpaperBlurTheme);
        Assert.True(loaded.HasMigratedToDefaultImageWallpaper);

        JsonObject persisted = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.Equal("Mica", persisted["appearance"]!["windowBackdrop"]!.GetValue<string>());
        Assert.Equal("Automatic", persisted["appearance"]!["accentColorMode"]!.GetValue<string>());
        Assert.Null(persisted["appearance"]!["customBackgroundImagePath"]);
    }

    [Fact]
    public void Load_AfterOlderBuildSavesFlatSettings_MergesChangesAndPreservesSectionOnlyData()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var manager = new SettingsManager(settingsPath);
        var initial = new AppSettings
        {
            AppRootPath = @"D:\PocketMC\Original",
            HasMigratedToGreenWallpaperBlurTheme = true,
            HasMigratedToDefaultImageWallpaper = true,
            WindowBackdrop = "Mica"
        };
        initial.AiApiKeys["Gemini"] = "existing-ai-key";
        manager.Save(initial);

        JsonObject sectioned = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        sectioned["appearance"]!["futureWallpaperMode"] = "Prismatic";
        sectioned["futureFeature"] = new JsonObject { ["enabled"] = true };
        string snapshotContent = sectioned.ToJsonString();
        File.WriteAllText(settingsPath, snapshotContent);
        File.WriteAllText(SettingsDocumentCodec.GetSnapshotPath(settingsPath), snapshotContent);

        AppSettings olderSettings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(sectioned));
        olderSettings.AppRootPath = @"E:\PocketMC\Updated";
        olderSettings.WindowBackdrop = "FakeMica";
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(olderSettings));

        AppSettings reloaded = manager.Load();

        Assert.Equal(@"E:\PocketMC\Updated", reloaded.AppRootPath);
        Assert.Equal("FakeMica", reloaded.WindowBackdrop);
        Assert.Equal("existing-ai-key", reloaded.AiApiKeys["Gemini"]);
        JsonObject upgraded = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.Equal("Prismatic", upgraded["appearance"]!["futureWallpaperMode"]!.GetValue<string>());
        Assert.True(upgraded["futureFeature"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Load_CorruptSettingsBlocksSaveAndPreservesOriginalFile()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        const string corruptContents = "{ invalid json corrupt content !!! ";
        File.WriteAllText(settingsPath, corruptContents);
        var manager = new SettingsManager(settingsPath);

        manager.Load();

        Assert.Throws<InvalidOperationException>(() => manager.Save(new AppSettings()));
        Assert.Equal(corruptContents, File.ReadAllText(settingsPath));
        Assert.Single(Directory.GetFiles(_tempDirectory, "settings.json.corrupted.*.bak"));
    }

    [Fact]
    public void Load_FutureFormatAllowsKnownValuesButBlocksWrites()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        const string futureContents = """
        {
          "formatVersion": 4,
          "application": { "appRootPath": "D:\\PocketMC\\Future" },
          "AppRootPath": "D:\\PocketMC\\Future",
          "SchemaVersion": 2
        }
        """;
        File.WriteAllText(settingsPath, futureContents);
        var manager = new SettingsManager(settingsPath);

        AppSettings loaded = manager.Load();

        Assert.Equal(@"D:\PocketMC\Future", loaded.AppRootPath);
        Assert.Throws<InvalidOperationException>(() => manager.Save(loaded));
        Assert.Equal(futureContents, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Save_WithCorruptRecoverySnapshot_RefusesToOverwriteLegacySettings()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        const string existingContents = "{ \"AppRootPath\": \"D:\\\\PocketMC\\\\Instances\" }";
        File.WriteAllText(settingsPath, existingContents);
        File.WriteAllText(SettingsDocumentCodec.GetSnapshotPath(settingsPath), "not json");
        var manager = new SettingsManager(settingsPath);

        Assert.Throws<InvalidOperationException>(() => manager.Save(new AppSettings()));
        Assert.Equal(existingContents, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Load_InvalidFormatVersionPreservesFileAndBlocksWrites()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        const string invalidVersion = "{ \"formatVersion\": \"three\", \"AppRootPath\": \"D:\\\\PocketMC\" }";
        File.WriteAllText(settingsPath, invalidVersion);
        var manager = new SettingsManager(settingsPath);

        manager.Load();

        Assert.Throws<InvalidOperationException>(() => manager.Save(new AppSettings()));
        Assert.Equal(invalidVersion, File.ReadAllText(settingsPath));
    }

    private static string CreateCorruptedProtectedPayload()
    {
        return "dpapi:v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

