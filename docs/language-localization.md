# Language Localization

This guide explains how PocketMC handles runtime localization and how to extend it for new languages.

## Supported Languages

- English (`en-US`)
- Spanish (`es-ES`)
- Hindi (`hi-IN`)
- French (`fr-FR`)
- German (`de-DE`)
- Japanese (`ja-JP`)

## Architecture Overview

PocketMC uses WPF `ResourceDictionary` files to store UI text for each supported culture. The localization flow is:

1. App startup loads a default resource dictionary.
2. `LocalizationService.Initialize(...)` reads the saved language code from user settings.
3. The current language resource dictionary is merged into `Application.Current.Resources`.
4. UI elements use `DynamicResource` for text values so changes apply immediately.

### Key files

- `PocketMC.Desktop/Resources/Strings.en-US.xaml`
- `PocketMC.Desktop/Resources/Strings.es-ES.xaml`
- `PocketMC.Desktop/Resources/Strings.hi-IN.xaml`
- `PocketMC.Desktop/Resources/Strings.fr-FR.xaml`
- `PocketMC.Desktop/Resources/Strings.de-DE.xaml`
- `PocketMC.Desktop/Resources/Strings.ja-JP.xaml`
- `PocketMC.Desktop/Infrastructure/LocalizationService.cs`
- `PocketMC.Desktop/Features/Setup/AppSettingsPage.xaml`

## Runtime behavior

`LocalizationService` performs two responsibilities:

- Loading the correct `Strings.<culture>.xaml` file
- Swapping it at runtime when the user changes language

Example behavior:

- `LoadResourceDictionary("es-ES")` removes the existing `Strings.*.xaml` dictionary from `Application.Current.Resources`.
- It then adds the new resource dictionary using a `pack://application:,,,` URI.
- UI controls bound with `DynamicResource` refresh automatically.

## WPF usage rules

### Use `DynamicResource`, not `StaticResource`

Always bind localized UI text with `DynamicResource`:

```xml
<ui:Button Content="{DynamicResource ConsoleSendButton}" />
<TextBlock Text="{DynamicResource NewInstanceAcceptEulaLabel}" />
```

`StaticResource` will not update after the language changes.

### Localized hyperlink text

In WPF, hyperlink text must be wrapped in a `Run`:

```xml
<Hyperlink NavigateUri="https://aka.ms/MinecraftEULA"
           RequestNavigate="MinecraftEulaLink_RequestNavigate">
    <Run Text="{DynamicResource NewInstanceEulaLinkText}" />
</Hyperlink>
```

If the hyperlink content is written as raw `{DynamicResource ...}` text, the resource token will show literally.

## Adding a new language

1. Copy `Strings.en-US.xaml` to a new file named `Strings.<culture>.xaml`.
2. Translate every key value in the new file.
3. Add an entry to `LocalizationService.SupportedLanguages`:

```csharp
public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } = new List<LanguageInfo>
{
    new LanguageInfo("en-US", "English"),
    new LanguageInfo("es-ES", "Español"),
    new LanguageInfo("hi-IN", "हिंदी"),
    new LanguageInfo("fr-FR", "Français"),
    new LanguageInfo("de-DE", "Deutsch"),
    new LanguageInfo("ja-JP", "日本語")
};
```

4. Add a `ComboBoxItem` to `PocketMC.Desktop/Features/Setup/AppSettingsPage.xaml`:

```xml
<ComboBoxItem Content="{DynamicResource LanguageFrench}" Tag="fr-FR" />
```

5. Add the new `Language<Label>` key to all existing resource dictionaries.

## Resource key conventions

Follow these best practices:

- Use a single key per literal string.
- Prefix keys with the feature or page name (`Console`, `Dashboard`, `NewInstance`, `Breadcrumb`).
- Keep keys stable even when the text changes.

Example:

```xml
<sys:String x:Key="ConsoleBackButton">Back</sys:String>
<sys:String x:Key="DashboardMetricCpuLabel">CPU</sys:String>
<sys:String x:Key="BreadcrumbSettings">Settings</sys:String>
```

## Troubleshooting

### Common issues

- `DynamicResource` not updating: check that the XAML binding is `DynamicResource`, not `StaticResource`.
- Missing key in one language file: add the key to every `Strings.*.xaml` file.
- Wrong page language after switching: ensure `LocalizationService.ChangeLanguage(...)` saved the selected code to settings.

### Special note for Hindi and RTL-like text

Hindi is left-to-right, but UI copy may wrap differently. Use `TextWrapping="Wrap"` and avoid hard-coded inline markup inside localized blocks.

## Advanced developer notes

### Resource dictionary lookup

The app searches for the current dictionary by name pattern `Strings.` and replaces it at runtime. This means:

- Only one `Strings.<culture>.xaml` dictionary should be merged at once.
- Theme and control style dictionaries can remain merged independently.

### Fallback behavior

If the selected language is not found, the system falls back to `en-US`.

### Adding new UI string keys

When adding a new page or control:

1. Create a new key in `Strings.en-US.xaml`.
2. Copy the key to every existing `Strings.*.xaml`.
3. Use `DynamicResource` in XAML.

## Example file structure

```
PocketMC.Desktop/
  Resources/
    Strings.en-US.xaml
    Strings.es-ES.xaml
    Strings.hi-IN.xaml
    Strings.fr-FR.xaml
    Strings.de-DE.xaml
    Strings.ja-JP.xaml
  Infrastructure/
    LocalizationService.cs
  Features/
    Setup/
      AppSettingsPage.xaml
    InstanceCreation/
      NewInstancePage.xaml
    Console/
      ServerConsolePage.xaml
```

## Summary

This localization system is designed for runtime language switching across PocketMC without requiring an app restart. The core requirement is correct `DynamicResource` usage and keeping every language resource file in sync with the same keys.
