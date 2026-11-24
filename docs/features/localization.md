# Localization (i18n)

PocketMC uses a gettext-style scheme: the English source text is the lookup key. A language pack
translates keys it knows; anything missing falls back to the English source, so pages can be
migrated incrementally without breaking the UI.

## Usage

XAML (add `xmlns:loc="clr-namespace:PocketMC.Desktop.Localization"` to the root element):

```xml
<TextBlock Text="{loc:Tr Dashboard}" />
<TextBlock Text="{loc:Tr 'Apps &amp; Tools'}" />   <!-- quote values containing commas or & -->
```

C#:

```csharp
using PocketMC.Desktop.Localization;

string a = L.T("Dashboard");
string b = L.F("Delete {0}?", serverName);
string c = L.Date(createdAt);
string d = L.DateTime(createdAt);
```

Never pass translated text into the key position. Bindings built with `{loc:Tr}` listen to
`LocalizationService.Version`, so switching the language refreshes them without restarting.
Computed strings in view models must raise `PropertyChanged` from a
`LanguageChanged` handler (see `InstanceCardViewModel`).

## Language packs

- Bundled: `PocketMC.Desktop/Assets/i18n/zh-CN.json`, embedded as
  `PocketMC.Desktop.i18n.zh-CN.json`.
- Overrides / extra languages: `i18n/*.json` next to the executable (wins) and
  `%LOCALAPPDATA%\PocketMC\i18n\*.json`. Files are read in filename order.

```json
{
  "language": "zh-CN",
  "displayName": "简体中文",
  "culture": "zh-CN",
  "dateFormat": "yyyy年M月d日",
  "dateTimeFormat": "yyyy年M月d日 HH:mm",
  "strings": {
    "Dashboard": "仪表盘"
  }
}
```

`dateFormat` / `dateTimeFormat` are optional (defaults `MMM d, yyyy` and `MMM d, yyyy h:mm tt`).
An unknown culture silently degrades to invariant formatting.

## Settings

`AppSettings.Language` stores `system`, `en`, or any catalog tag (e.g. `zh-CN`). The app resolves
the preference at startup (`Program.Main` → `LocalizationService`), and the App Settings page
exposes a "Language & Region" card that saves immediately and switches live. Only
`CurrentUICulture` is changed — `CurrentCulture` stays untouched so parsing and serialization
remain invariant.

## Adding a language

1. Copy `zh-CN.json` to `<tag>.json`, translate `strings`.
2. Drop it beside the executable (`i18n/`) or add it under `Assets/i18n/` to embed it in the build.
3. Rebuild; the language appears in the App Settings selector automatically.

## Tests

`PocketMC.Desktop.Tests/Localization/LocalizationServiceTests.cs` covers catalog parsing,
fallback, formatting, language switching, and the shipped `zh-CN.json`.
