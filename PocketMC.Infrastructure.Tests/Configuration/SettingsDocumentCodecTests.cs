using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

namespace PocketMC.Infrastructure.Tests.Configuration;

public sealed class SettingsDocumentCodecTests
{
    [Fact]
    public void EveryWritableAppSettingHasASection()
    {
        PropertyInfo[] properties = typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.All(
            properties.Where(property => property.CanRead && property.CanWrite),
            property => Assert.True(
                SettingsDocumentCodec.HasSectionForProperty(property.Name),
                $"AppSettings.{property.Name} needs an assigned JSON section."));
    }

    [Fact]
    public void Serialize_GroupsSettingsAndRetainsLegacyCompatibilityKeys()
    {
        AppSettings settings = new()
        {
            AppRootPath = @"D:\PocketMC\Instances",
            WindowBackdrop = "Custom",
            CustomAccentColor = "#123456",
            EnableAiSummarization = false,
            CurseForgeApiKey = "dpapi:v2:protected-curseforge",
            DiscordApiKey = "dpapi:v1:protected-discord"
        };
        settings.AiApiKeys["Gemini"] = "dpapi:v2:protected-ai";

        JsonObject document = SettingsDocumentCodec.Serialize(settings);

        Assert.Equal(3, document[SettingsDocumentCodec.FormatVersionProperty]!.GetValue<int>());
        Assert.Equal(settings.AppRootPath, document["application"]!["appRootPath"]!.GetValue<string>());
        Assert.Equal("Custom", document["appearance"]!["windowBackdrop"]!.GetValue<string>());
        Assert.False(document["ai"]!["enableAiSummarization"]!.GetValue<bool>());
        Assert.Equal("dpapi:v2:protected-ai", document["ai"]!["aiApiKeys"]!["Gemini"]!.GetValue<string>());
        Assert.Equal("dpapi:v2:protected-curseforge", document["marketplace"]!["curseForgeApiKey"]!.GetValue<string>());
        Assert.Equal("dpapi:v1:protected-discord", document["discord"]!["discordApiKey"]!.GetValue<string>());
        Assert.Equal(settings.AppRootPath, document[nameof(AppSettings.AppRootPath)]!.GetValue<string>());
        Assert.Equal(2, document[nameof(AppSettings.SchemaVersion)]!.GetValue<int>());
    }

    [Fact]
    public void ToLegacyFlat_PreservesRemoteControlSettingsFromSectionedDocument()
    {
        AppSettings original = new();
        original.RemoteControl.Enabled = true;
        original.RemoteControl.Username = "saved-user";
        original.RemoteControl.PasswordHash = "saved-password-hash";
        original.RemoteControl.ProtectedPassword = "saved-protected-password";
        original.RemoteControl.RequireAuthentication = false;
        original.RemoteControl.Users.Add(new RemoteControlUser
        {
            Username = "saved-subuser",
            PasswordHash = "saved-subuser-hash"
        });

        JsonObject document = SettingsDocumentCodec.Serialize(original);
        AppSettings loaded = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(document));

        Assert.True(loaded.RemoteControl.Enabled);
        Assert.Equal("saved-user", loaded.RemoteControl.Username);
        Assert.Equal("saved-password-hash", loaded.RemoteControl.PasswordHash);
        Assert.Equal("saved-protected-password", loaded.RemoteControl.ProtectedPassword);
        Assert.False(loaded.RemoteControl.RequireAuthentication);
        Assert.Single(loaded.RemoteControl.Users);
        Assert.Equal("saved-subuser", loaded.RemoteControl.Users[0].Username);
        Assert.Equal("saved-subuser-hash", loaded.RemoteControl.Users[0].PasswordHash);
    }

    [Fact]
    public void MergeLegacyValues_AppliesKnownChangesAndPreservesNewerSections()
    {
        AppSettings original = new()
        {
            AppRootPath = @"D:\Old\Instances",
            WindowBackdrop = "FakeMica"
        };
        original.AiApiKeys["Gemini"] = "dpapi:v2:protected-ai";

        JsonObject snapshot = SettingsDocumentCodec.Serialize(original);
        snapshot["appearance"]!["futureWallpaperMode"] = "Prismatic";
        snapshot["futureFeature"] = new JsonObject { ["enabled"] = true };

        AppSettings oldBuildSettings = SettingsDocumentCodec.DeserializeFlat(
            SettingsDocumentCodec.ToLegacyFlat(snapshot));
        oldBuildSettings.AppRootPath = @"E:\Updated\Instances";
        oldBuildSettings.WindowBackdrop = "Mica";
        JsonObject oldBuildSave = JsonNode.Parse(JsonSerializer.Serialize(oldBuildSettings))!.AsObject();

        JsonObject merged = SettingsDocumentCodec.MergeLegacyValues(snapshot, oldBuildSave);
        JsonObject mergedFlat = SettingsDocumentCodec.ToLegacyFlat(merged);
        AppSettings restored = SettingsDocumentCodec.DeserializeFlat(mergedFlat);

        Assert.Equal(@"E:\Updated\Instances", restored.AppRootPath);
        Assert.Equal("Mica", restored.WindowBackdrop);
        Assert.Equal("dpapi:v2:protected-ai", restored.AiApiKeys["Gemini"]);
        Assert.Equal("Prismatic", merged["appearance"]!["futureWallpaperMode"]!.GetValue<string>());
        Assert.True(merged["futureFeature"]!["enabled"]!.GetValue<bool>());
    }
}