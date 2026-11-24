using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace PocketMC.Desktop.Localization;

public sealed class LanguageChangedEventArgs : EventArgs
{
    public LanguageChangedEventArgs(string language, string preference)
    {
        Language = language;
        Preference = preference;
    }

    public string Language { get; }
    public string Preference { get; }
}

public sealed record LanguageOption(string Tag, string DisplayName);

/// <summary>
/// Singleton translation catalog holder. Keys are English source strings so missing entries
/// fall back to readable English while languages are migrated incrementally.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string SystemPreference = "system";
    public const string FallbackLanguage = "en";

    private const string EmbeddedPrefix = "PocketMC.Desktop.i18n.";
    private static readonly Lazy<LocalizationService> Shared = new(CreateDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, LanguageCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly CultureInfo _systemCulture = CultureInfo.CurrentUICulture;
    private volatile LanguageCatalog? _active;
    private volatile LanguageCatalog? _fallback;
    private string _preference = SystemPreference;
    private int _version;

    public LocalizationService()
    {
    }

    public static LocalizationService Instance => Shared.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <summary>Incremented on every language switch; bindings listen to this to re-read strings.</summary>
    public int Version => Volatile.Read(ref _version);

    public string Preference => _preference;

    public string Language => _active?.Language ?? FallbackLanguage;

    public CultureInfo Culture => _active?.Culture ?? CultureInfo.GetCultureInfo("en-US");

    public IReadOnlyList<LanguageOption> Languages => BuildLanguageOptions();

    public static string T(string source) => Instance.Get(source);

    public static string F(string source, params object?[] args) => Instance.Format(source, args);

    public string Get(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        LanguageCatalog? active = _active;
        if (active != null && active.Strings.TryGetValue(source, out string? translated))
        {
            return translated;
        }

        LanguageCatalog? fallback = _fallback;
        if (fallback != null && fallback.Strings.TryGetValue(source, out translated))
        {
            return translated;
        }

        return source;
    }

    public string Format(string source, params object?[] args)
    {
        string pattern = Get(source);
        if (args.Length == 0)
        {
            return pattern;
        }

        try
        {
            return string.Format(Culture, pattern, args);
        }
        catch (FormatException ex)
        {
            // A broken placeholder must never crash the UI thread; fall back to the raw pattern.
            Trace.TraceWarning($"Localization format failure for '{source}': {ex.Message}");
            return pattern;
        }
    }

    public string FormatDate(DateTime value) => value.ToString(ActiveDateFormat, Culture);

    public string FormatDate(DateTimeOffset value) => value.ToString(ActiveDateFormat, Culture);

    public string FormatDateTime(DateTime value) => value.ToString(ActiveDateTimeFormat, Culture);

    public string FormatDateTime(DateTimeOffset value) => value.ToString(ActiveDateTimeFormat, Culture);

    private string ActiveDateFormat => _active?.DateFormat ?? LanguageCatalog.DefaultDateFormat;

    private string ActiveDateTimeFormat => _active?.DateTimeFormat ?? LanguageCatalog.DefaultDateTimeFormat;

    /// <summary>Registers or replaces a catalog. Used by language packs loaded from disk.</summary>
    public void RegisterCatalog(LanguageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_gate)
        {
            _catalogs[catalog.Language] = catalog;
            _fallback = ResolveTag(FallbackLanguage);
        }

        if (_active?.Language.Equals(catalog.Language, StringComparison.OrdinalIgnoreCase) == true)
        {
            _active = catalog;
            ApplyUiCulture(catalog);
            NotifyLanguageChanged();
        }
    }

    /// <summary>Switches language. Accepts "system", "en", "zh-CN" or any registered tag.</summary>
    public void SetLanguage(string? preference)
    {
        string normalized = NormalizePreference(preference);
        LanguageCatalog? resolved;
        lock (_gate)
        {
            resolved = Resolve(normalized);
            _fallback = ResolveTag(FallbackLanguage);
        }

        string previousLanguage = Language;
        bool preferenceChanged = !string.Equals(_preference, normalized, StringComparison.OrdinalIgnoreCase);
        _preference = normalized;
        _active = resolved;
        ApplyUiCulture(resolved);

        if (preferenceChanged || !string.Equals(previousLanguage, Language, StringComparison.OrdinalIgnoreCase))
        {
            NotifyLanguageChanged();
        }
    }

    /// <summary>Loads bundled catalogs plus optional overrides from the app and user data folders.</summary>
    public void LoadCatalogs(Assembly? assembly = null)
    {
        assembly ??= typeof(LocalizationService).Assembly;
        LoadEmbeddedCatalogs(assembly);
        LoadExternalCatalogs(Path.Combine(AppContext.BaseDirectory, "i18n"));
        LoadExternalCatalogs(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PocketMC",
            "i18n"));
    }

    private void LoadEmbeddedCatalogs(Assembly assembly)
    {
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(EmbeddedPrefix, StringComparison.Ordinal)
                || !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                LoadCatalogText(reader.ReadToEnd(), resourceName);
            }
            catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException)
            {
                Trace.TraceWarning($"Failed to load embedded language '{resourceName}': {ex.Message}");
            }
        }
    }

    private void LoadExternalCatalogs(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    LoadCatalogText(File.ReadAllText(file), file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
                {
                    Trace.TraceWarning($"Failed to load language '{file}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to scan language folder '{directory}': {ex.Message}");
        }
    }

    private void LoadCatalogText(string json, string origin)
    {
        LanguageCatalog? catalog = LanguageCatalog.TryParse(json, out string? error);
        if (catalog is null)
        {
            Trace.TraceWarning($"Invalid language catalog '{origin}': {error}");
            return;
        }

        lock (_gate)
        {
            _catalogs[catalog.Language] = catalog;
        }
    }

    private void ApplyUiCulture(LanguageCatalog? catalog)
    {
        if (catalog is null || catalog.Culture.Equals(CultureInfo.InvariantCulture))
        {
            return;
        }

        try
        {
            // UI culture drives formatting; CurrentCulture stays untouched so parsing/serialization remain stable.
            CultureInfo.CurrentUICulture = catalog.Culture;
        }
        catch (ArgumentException)
        {
            // Culture may be unavailable on stripped-down systems; English text still works.
        }
    }

    private void NotifyLanguageChanged()
    {
        Interlocked.Increment(ref _version);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preference)));
        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(Language, _preference));
    }

    private LanguageCatalog? Resolve(string preference)
    {
        if (preference.Equals(SystemPreference, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSystem();
        }

        // Unknown tag falls back to English source strings instead of guessing a language.
        return ResolveTag(preference);
    }

    private LanguageCatalog? ResolveSystem()
    {
        for (CultureInfo? culture = _systemCulture; culture is not null && !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            LanguageCatalog? match = ResolveTag(culture.Name);
            if (match != null)
            {
                return match;
            }
        }

        return ResolveTag(FallbackLanguage);
    }

    private LanguageCatalog? ResolveTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        if (_catalogs.TryGetValue(tag, out LanguageCatalog? exact))
        {
            return exact;
        }

        string language = tag.Split('-')[0];
        foreach (LanguageCatalog catalog in _catalogs.Values)
        {
            if (catalog.Language.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                return catalog;
            }
        }

        return null;
    }

    private IReadOnlyList<LanguageOption> BuildLanguageOptions()
    {
        var options = new List<LanguageOption>
        {
            new(SystemPreference, $"{Get("System default")} ({_systemCulture.DisplayName})"),
        };

        lock (_gate)
        {
            bool hasEnglishCatalog = _catalogs.ContainsKey(FallbackLanguage);
            if (!hasEnglishCatalog)
            {
                // English lives in the source strings; it is still selectable explicitly.
                options.Add(new LanguageOption(FallbackLanguage, "English"));
            }

            foreach (LanguageCatalog catalog in _catalogs.Values
                .Where(static catalog => !catalog.Language.Equals(FallbackLanguage, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static catalog => catalog.Language, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(new LanguageOption(catalog.Language, catalog.DisplayName));
            }
        }

        return options;
    }

    private static string NormalizePreference(string? preference)
        => string.IsNullOrWhiteSpace(preference) ? SystemPreference : preference.Trim();

    private static LocalizationService CreateDefault()
    {
        var service = new LocalizationService();
        service.LoadCatalogs();
        service.SetLanguage(SystemPreference);
        return service;
    }
}
