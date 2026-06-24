using System.Text.Json;
using System.Security.Cryptography;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Tests.RemoteControl;

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
        Assert.False(settings.RemoteControl.AllowRemoteConsoleCommands);
        Assert.True(settings.RemoteControl.AllowRemotePlayerActions);
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}



