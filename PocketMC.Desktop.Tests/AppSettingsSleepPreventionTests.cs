using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class AppSettingsSleepPreventionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewAppSettings_DefaultsKeepComputerAwakeWhileServersRunningToTrue()
    {
        var settings = new AppSettings();

        Assert.True(settings.KeepComputerAwakeWhileServersRunning);
    }

    [Fact]
    public void Load_WhenSettingsJsonOmitsSleepPreventionSetting_DefaultsToTrue()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "AppRootPath": "D:\\PocketMC\\Instances",
          "MinimizeToTrayOnClose": true
        }
        """);

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.True(loaded.KeepComputerAwakeWhileServersRunning);
    }

    [Fact]
    public void Load_WhenSettingsJsonDisablesSleepPrevention_PreservesExplicitFalse()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "KeepComputerAwakeWhileServersRunning": false
        }
        """);

        AppSettings loaded = new SettingsManager(settingsPath).Load();

        Assert.False(loaded.KeepComputerAwakeWhileServersRunning);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
