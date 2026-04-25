using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Infrastructure
{
    public sealed class LocalizationService
    {
        private readonly SettingsManager _settingsManager;

        public LocalizationService(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } = new List<LanguageInfo>
        {
            new LanguageInfo("en-US", "English"),
            new LanguageInfo("es-ES", "Español"),
            new LanguageInfo("fr-FR", "Français"),
            new LanguageInfo("de-DE", "Deutsch"),
            new LanguageInfo("ja-JP", "日本語"),
            new LanguageInfo("zh-CN", "中文")
        };

        public string CurrentLanguageCode { get; private set; } = "en-US";

        public void Initialize(string? languageCode)
        {
            var code = DetermineInitialLanguage(languageCode);
            if (!SupportedLanguages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                code = "en-US";
            }

            CurrentLanguageCode = code;
            LoadResourceDictionary(code);
        }

        private string DetermineInitialLanguage(string? languageCode)
        {
            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                return languageCode;
            }

            var systemCulture = CultureInfo.CurrentUICulture;
            var exactMatch = SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, systemCulture.Name, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch.Code;
            }

            var neutralPrefix = systemCulture.TwoLetterISOLanguageName + "-";
            var partialMatch = SupportedLanguages.FirstOrDefault(l => l.Code.StartsWith(neutralPrefix, StringComparison.OrdinalIgnoreCase));
            if (partialMatch != null)
            {
                return partialMatch.Code;
            }

            return "en-US";
        }

        public void ChangeLanguage(string languageCode)
        {
            if (string.Equals(CurrentLanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!SupportedLanguages.Any(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase)))
            {
                languageCode = "en-US";
            }

            CurrentLanguageCode = languageCode;
            LoadResourceDictionary(languageCode);

            var settings = _settingsManager.Load();
            settings.Language = CurrentLanguageCode;
            _settingsManager.Save(settings);
        }

        public string GetString(string key)
        {
            if (Application.Current?.Resources?.Contains(key) == true)
            {
                return Application.Current.Resources[key]?.ToString() ?? key;
            }

            return key;
        }

        private ResourceDictionary? _baseLanguageDictionary;
        private ResourceDictionary? _activeLanguageDictionary;

        private void LoadResourceDictionary(string languageCode)
        {
            if (Application.Current == null) return;

            var appResources = Application.Current.Resources;

            // Always ensure en-US is loaded as the base
            _baseLanguageDictionary ??= CreateDictionary("en-US");
            if (!appResources.MergedDictionaries.Contains(_baseLanguageDictionary))
                appResources.MergedDictionaries.Add(_baseLanguageDictionary);

            // Remove previous overlay (non-base language)
            if (_activeLanguageDictionary != null)
            {
                appResources.MergedDictionaries.Remove(_activeLanguageDictionary);
                _activeLanguageDictionary = null;
            }

            if (string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                _activeLanguageDictionary = CreateDictionary(languageCode);
                appResources.MergedDictionaries.Add(_activeLanguageDictionary);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Failed to load language dictionary for {languageCode}: {ex}");
                CurrentLanguageCode = "en-US";
            }
        }

        private static ResourceDictionary CreateDictionary(string languageCode) =>
            new() { Source = new Uri(
                $"pack://application:,,,/PocketMC.Desktop;component/Resources/Strings.{languageCode}.xaml",
                UriKind.Absolute) };
    }

    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; }
        public string DisplayName { get; }
    }
}
