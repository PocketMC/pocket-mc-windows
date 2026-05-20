using PocketMC.Desktop.Features.CloudBackups;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Models;

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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
