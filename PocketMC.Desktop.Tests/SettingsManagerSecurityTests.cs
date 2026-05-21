using PocketMC.Desktop.Features.CloudBackups;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace PocketMC.Desktop.Tests;

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
        Assert.Contains("dpapi:v1:", persisted, StringComparison.Ordinal);

        AppSettings loaded = manager.Load();
        Assert.Equal("access-token-plain", loaded.CloudTokens["GoogleDrive"].AccessToken);
        Assert.Equal("refresh-token-plain", loaded.CloudTokens["GoogleDrive"].RefreshToken);
    }

    [Fact]
    public void Load_ClearsOnlyCorruptedProtectedSecretAndPreservesOtherSettings()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var settings = new AppSettings
        {
            AppRootPath = @"D:\PocketMC\Instances",
            CurseForgeApiKey = CreateCorruptedProtectedPayload(),
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
