using System.Text.Json;
using System.Windows.Media;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class AccentColorSettingsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewSettings_DefaultToCustomGreenAccent()
    {
        var settings = new AppSettings();

        Assert.Equal(AccentColorService.CustomMode, settings.AccentColorMode);
        Assert.Equal("#008B00", settings.CustomAccentColor);
    }

    [Theory]
    [InlineData("#E3008C", 0xE3, 0x00, 0x8C, "#E3008C")]
    [InlineData("ca5010", 0xCA, 0x50, 0x10, "#CA5010")]
    public void TryParseHexColor_AcceptsSixDigitRgbHex(string input, byte red, byte green, byte blue, string normalized)
    {
        bool parsed = AccentColorService.TryParseHexColor(input, out Color color, out string normalizedHex);

        Assert.True(parsed);
        Assert.Equal(Color.FromRgb(red, green, blue), color);
        Assert.Equal(normalized, normalizedHex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("ZZZZZZ")]
    [InlineData("#12-456")]
    public void TryParseHexColor_RejectsInvalidHex(string input)
    {
        bool parsed = AccentColorService.TryParseHexColor(input, out _, out string normalizedHex);

        Assert.False(parsed);
        Assert.Equal(string.Empty, normalizedHex);
    }

    [Fact]
    public void SettingsManager_PersistsCustomAccentAsPlainNonSecretSetting()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var settings = new AppSettings
        {
            AccentColorMode = AccentColorService.CustomMode,
            CustomAccentColor = "#CA5010"
        };

        new SettingsManager(settingsPath).Save(settings);

        string persisted = File.ReadAllText(settingsPath);
        Assert.Contains("\"AccentColorMode\": \"Custom\"", persisted);
        Assert.Contains("\"CustomAccentColor\": \"#CA5010\"", persisted);
        Assert.DoesNotContain("dpapi:v1:#CA5010", persisted, StringComparison.Ordinal);

        AppSettings loaded = new SettingsManager(settingsPath).Load();
        Assert.Equal(AccentColorService.CustomMode, loaded.AccentColorMode);
        Assert.Equal("#CA5010", loaded.CustomAccentColor);
    }

    [Fact]
    public void DeserializedLegacySettings_DefaultToCustomGreenAccent()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}");

        Assert.NotNull(settings);
        Assert.Equal(AccentColorService.CustomMode, settings!.AccentColorMode);
        Assert.Equal("#008B00", settings.CustomAccentColor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}


