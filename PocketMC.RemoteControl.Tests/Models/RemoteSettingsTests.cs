using PocketMC.Infrastructure.Configuration;
using System.Text.Json;
using System.Security.Cryptography;
using PocketMC.Domain.Models;

namespace PocketMC.RemoteControl.Tests.Models;

public sealed class RemoteSettingsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_NormalizesRemoteControlWithSafeDefaults()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{}");

        var settings = new SettingsManager(settingsPath).Load();

        Assert.NotNull(settings.RemoteControl);
        Assert.False(settings.RemoteControl.Enabled);
        Assert.Equal(25580, settings.RemoteControl.Port);
        Assert.Equal(RemoteAccessMode.CloudflaredQuickTunnel, settings.RemoteControl.AccessMode);
        Assert.Equal("cloudflared-quick", settings.RemoteControl.TunnelProviderId);
        Assert.Null(settings.RemoteControl.PlayitTunnelId);
    }

    [Fact]
    public void Save_PersistsRemoteControl()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var settings = new SettingsManager(settingsPath).Load();

        new SettingsManager(settingsPath).Save(settings);

        string persisted = File.ReadAllText(settingsPath);
        Assert.Contains("RemoteControl", persisted, StringComparison.Ordinal);

        var roundTripped = JsonSerializer.Deserialize<Dictionary<string, object>>(persisted);
        Assert.NotNull(roundTripped);
    }

    [Fact]
    public void Load_PreservesExistingCredentialsAndMigratesToSchemaVersion2()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "RemoteControl": {
            "Enabled": true,
            "Port": 25590,
            "Username": "savedAdmin",
            "PasswordHash": "salt123:hash456",
            "RequireAuthentication": true,
            "Users": [
              {
                "Username": "savedSubUser",
                "PasswordHash": "subsalt:subhash"
              }
            ]
          }
        }
        """);

        var settings = new SettingsManager(settingsPath).Load();

        Assert.Equal(2, settings.SchemaVersion);
        Assert.NotNull(settings.RemoteControl);
        Assert.True(settings.RemoteControl.Enabled);
        Assert.Equal(25590, settings.RemoteControl.Port);
        Assert.Equal("savedAdmin", settings.RemoteControl.Username);
        Assert.Equal("salt123:hash456", settings.RemoteControl.PasswordHash);
        Assert.Single(settings.RemoteControl.Users);
        Assert.Equal("savedSubUser", settings.RemoteControl.Users[0].Username);
        Assert.Equal("subsalt:subhash", settings.RemoteControl.Users[0].PasswordHash);
        Assert.NotNull(settings.RemoteControl.Users[0].AllowedInstanceIds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}


