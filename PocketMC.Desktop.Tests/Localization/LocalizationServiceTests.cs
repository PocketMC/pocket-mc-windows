using System.Text.Json;
using PocketMC.Desktop.Localization;

namespace PocketMC.Desktop.Tests.Localization;

public sealed class LanguageCatalogTests
{
    private const string SampleJson = """
        {
          "language": "zh-CN",
          "displayName": "简体中文",
          "culture": "zh-CN",
          "dateFormat": "yyyy年M月d日",
          "strings": {
            "Dashboard": "仪表盘",
            "Delete {0}": "删除 {0}"
          }
        }
        """;

    [Fact]
    public void Parse_ReadsMetadataAndStrings()
    {
        LanguageCatalog catalog = LanguageCatalog.Parse(SampleJson);

        Assert.Equal("zh-CN", catalog.Language);
        Assert.Equal("简体中文", catalog.DisplayName);
        Assert.Equal("zh-CN", catalog.Culture.Name);
        Assert.Equal("yyyy年M月d日", catalog.DateFormat);
        Assert.Equal("仪表盘", catalog.Strings["Dashboard"]);
        Assert.Equal(LanguageCatalog.DefaultDateTimeFormat, catalog.DateTimeFormat);
    }

    [Fact]
    public void Parse_UsesInvariantCulture_WhenCultureIsUnknown()
    {
        LanguageCatalog catalog = LanguageCatalog.Parse("""{ "language": "xx-YY", "culture": "###" }""");

        Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, catalog.Culture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{ "displayName": "No language" }""")]
    [InlineData("{ not json")]
    public void TryParse_RejectsInvalidCatalogs(string json)
    {
        LanguageCatalog? catalog = LanguageCatalog.TryParse(json, out string? error);

        Assert.Null(catalog);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}

public sealed class LocalizationServiceTests
{
    private static LocalizationService CreateService(string language, string displayName, string stringsJson)
    {
        var service = new LocalizationService();
        string json = $$"""
            {
              "language": "{{language}}",
              "displayName": "{{displayName}}",
              "culture": "{{language}}",
              "dateFormat": "yyyy-MM-dd",
              "strings": {{stringsJson}}
            }
            """;
        service.RegisterCatalog(LanguageCatalog.Parse(json));
        return service;
    }

    [Fact]
    public void Get_FallsBackToSourceText_WhenKeyIsMissing()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", """{ "Dashboard": "仪表盘" }""");
        service.SetLanguage("zh-CN");

        Assert.Equal("仪表盘", service.Get("Dashboard"));
        Assert.Equal("Untranslated", service.Get("Untranslated"));
        Assert.Equal(string.Empty, service.Get(string.Empty));
    }

    [Fact]
    public void Format_UsesCatalogCultureAndTranslations()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", """{ "Delete {0}": "删除 {0}" }""");
        service.SetLanguage("zh-CN");

        Assert.Equal("删除 Minecraft", service.Format("Delete {0}", "Minecraft"));
    }

    [Fact]
    public void FormatDate_UsesCatalogDateFormat()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", "{}");
        service.SetLanguage("zh-CN");

        Assert.Equal("2024-05-06", service.FormatDate(new DateTime(2024, 5, 6)));
    }

    [Fact]
    public void SetLanguage_NotifiesOnlyWhenLanguageChanges()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", """{ "Dashboard": "仪表盘" }""");
        int changes = 0;
        service.LanguageChanged += (_, _) => changes++;

        service.SetLanguage("zh-CN");
        service.SetLanguage("zh-CN");
        Assert.Equal(1, changes);
        Assert.Equal("zh-CN", service.Language);
        Assert.Equal("zh-CN", service.Preference);

        service.SetLanguage("en");
        Assert.Equal(2, changes);
        Assert.Equal("en", service.Language);
        Assert.Equal(2, service.Version);
    }

    [Fact]
    public void SetLanguage_UnknownTagFallsBackToSourceText()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", """{ "Dashboard": "仪表盘" }""");

        service.SetLanguage("de-DE");

        Assert.Equal("Dashboard", service.Get("Dashboard"));
        Assert.Equal("de-DE", service.Preference);
    }

    [Fact]
    public void SetLanguage_SystemPreferenceKeepsPreferredTag()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", """{ "Dashboard": "仪表盘" }""");

        service.SetLanguage("  ");

        Assert.Equal(LocalizationService.SystemPreference, service.Preference);
        // Machine UI culture decides the resolved language; either way lookups stay safe.
        Assert.Contains(service.Get("Dashboard"), new[] { "Dashboard", "仪表盘" });
    }

    [Fact]
    public void Languages_ListsSystemDefaultAndCatalogs()
    {
        LocalizationService service = CreateService("zh-CN", "简体中文", "{}");

        IReadOnlyList<LanguageOption> options = service.Languages;

        Assert.Equal(LocalizationService.SystemPreference, options[0].Tag);
        Assert.Contains(options, option => option.Tag == "zh-CN" && option.DisplayName == "简体中文");
        Assert.Contains(options, option => option.Tag == LocalizationService.FallbackLanguage);
    }

    [Fact]
    public void EmbeddedChineseCatalog_LoadsAndTranslatesDashboard()
    {
        LocalizationService service = new();
        service.LoadCatalogs(typeof(LocalizationService).Assembly);
        service.SetLanguage("zh-CN");

        Assert.Equal("zh-CN", service.Language);
        Assert.Equal("实例", service.Get("Instances"));
    }

    [Fact]
    public void EmbeddedCatalog_IsValidJson()
    {
        // Guards the shipped zh-CN.json against hand-edit syntax errors.
        var assembly = typeof(LocalizationService).Assembly;
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource == "PocketMC.Desktop.i18n.zh-CN.json");
        Assert.NotNull(name);

        using Stream? stream = assembly.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        using JsonDocument document = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
