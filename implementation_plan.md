# Custom Accent Color Feature — Implementation Plan

Add a user-selectable accent color to PocketMC, independent of Windows personalization. Users can choose "Automatic (Windows)" to follow their system accent, or pick a custom color from presets or a hex input. The existing theme/backdrop system is untouched — only the accent color source changes.

---

## Proposed Changes

### Settings Model

#### [MODIFY] [AppSettings.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Models/AppSettings.cs)

Add two properties alongside the existing `WindowBackdrop`:

```csharp
public string AccentColorMode { get; set; } = "Automatic";   // "Automatic" | "Custom"
public string? CustomAccentColor { get; set; }                 // Hex string, e.g. "#0078D4"
```

`AccentColorMode = "Automatic"` is the default — identical to current behavior (Windows system accent). When `"Custom"`, the app uses `CustomAccentColor` and ignores Windows changes.

---

### New Accent Color Service

#### [NEW] [AccentColorService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Shell/AccentColorService.cs)

A singleton service that wraps `Wpf.Ui.Appearance.ApplicationAccentColorManager` and centralizes all accent color logic. Responsibilities:

1. **`ApplyCurrentAccent()`** — reads `AppSettings.AccentColorMode` and applies either system accent or custom accent. Must be called after every `ApplicationThemeManager.Apply()` call, because theme switches reset accent resources.
2. **`ApplyCustomAccent(Color color)`** — calls `ApplicationAccentColorManager.Apply(color, currentTheme)` to set a custom accent with correct Light/Dark variant generation.
3. **`ApplySystemAccent()`** — calls `ApplicationAccentColorManager.ApplySystemAccent()` to revert to the Windows accent.
4. **`GetCurrentTheme()`** — helper that reads the current `ApplicationTheme` from `ApplicationThemeManager` to pass the correct theme parameter for variant calculation.
5. **Event: `AccentChanged`** — fires when accent changes so other components (e.g., `AnimatedNavIndicatorBehavior`, `MarkdownConfig`) can react.

```csharp
public sealed class AccentColorService
{
    private readonly ApplicationState _applicationState;

    public event Action<Color>? AccentChanged;

    public AccentColorService(ApplicationState applicationState)
    {
        _applicationState = applicationState;
    }

    public void ApplyCurrentAccent()
    {
        var settings = _applicationState.Settings;
        if (settings.AccentColorMode == "Custom"
            && !string.IsNullOrEmpty(settings.CustomAccentColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(settings.CustomAccentColor);
                ApplyCustomAccent(color);
                return;
            }
            catch { /* Invalid hex — fall through to system */ }
        }
        ApplySystemAccent();
    }

    public void ApplyCustomAccent(Color color)
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        ApplicationAccentColorManager.Apply(color, theme);
        AccentChanged?.Invoke(color);
    }

    public void ApplySystemAccent()
    {
        ApplicationAccentColorManager.ApplySystemAccent();
        AccentChanged?.Invoke(ApplicationAccentColorManager.GetColorizationColor());
    }
}
```

> [!IMPORTANT]
> **Why a dedicated service instead of adding methods to `ShellVisualService`?**
> `ShellVisualService` is tightly coupled to the `FluentWindow` instance (via `Attach()`) and manages backdrops/DWM. Accent colors are application-global (they modify `Application.Current.Resources`) and don't need a window reference. A separate service keeps concerns clean and allows injection into any page/component that needs accent awareness.

---

### ShellVisualService Integration

#### [MODIFY] [ShellVisualService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Shell/ShellVisualService.cs)

1. **Inject `AccentColorService`** into the constructor.
2. **Call `_accentColorService.ApplyCurrentAccent()`** at the end of `ApplyTheme()`, after `ApplicationThemeManager.Apply()`. This is critical because `ApplicationThemeManager.Apply()` resets accent-related resources to system defaults.
3. **Clean up dead code**:
   - Remove the unused `string theme = "Dark"` parameter from `ApplyTheme()` (it's never used — the method always reads from `_applicationState.Settings.WindowBackdrop`).
   - Remove the no-op `SystemThemeWatcher.UnWatch(window)` call (line 146) — `Watch()` is never called, so `UnWatch()` does nothing.

```diff
- public void ApplyTheme(string theme = "Dark")
+ public void ApplyTheme()
  {
      // ... existing theme application ...
      ApplicationThemeManager.Apply(
          explicitLightMode
              ? ApplicationTheme.Light
              : ApplicationTheme.Dark);
+
+     _accentColorService.ApplyCurrentAccent();
  }
```

#### [MODIFY] [IShellVisualService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Shell/Interfaces/IShellVisualService.cs)

Update the interface to remove the dead parameter:

```diff
- void ApplyTheme(string theme = "Dark");
+ void ApplyTheme();
```

---

### DI Registration

#### [MODIFY] [ServiceCollectionExtensions.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Composition/ServiceCollectionExtensions.cs)

Register `AccentColorService` as a singleton alongside `ShellVisualService`:

```csharp
services.AddSingleton<AccentColorService>();
```

---

### Settings UI — Accent Color Section

#### [MODIFY] [AppSettingsPage.xaml](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Setup/AppSettingsPage.xaml)

Add a new **Accent Color** section inside the existing Appearance `CardExpander`, below the Custom Background panel. The section contains:

1. **Section header** — "Accent Color" title + subtitle.
2. **Radio buttons** — "Automatic (Windows Accent Color)" and "Custom".
3. **Color swatch grid** — 16 preset colors covering the spectrum, shown only in Custom mode.
4. **Hex input** — A `TextBox` for power users to enter an exact hex code, shown only in Custom mode.
5. **Reset button** — Reverts to Automatic mode, shown only in Custom mode.
6. **Active color indicator** — A small circle showing the current accent color regardless of mode.

```xml
<!-- Accent Color Section -->
<Separator Margin="0,16,0,12" Opacity="0.15"/>
<TextBlock Text="Accent Color" FontSize="14" FontWeight="Medium"/>
<TextBlock Text="Choose the accent color used for interactive controls, highlights, and selections."
           Foreground="{DynamicResource TextFillColorSecondaryBrush}" FontSize="12"
           Margin="0,4,0,12" TextWrapping="Wrap"/>

<!-- Current Accent Preview + Mode -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,12">
    <Border x:Name="AccentPreviewSwatch" Width="20" Height="20" CornerRadius="10"
            Margin="0,0,10,0" VerticalAlignment="Center"/>
    <TextBlock x:Name="AccentModeLabel" Text="Using Windows accent color"
               VerticalAlignment="Center" FontSize="12"
               Foreground="{DynamicResource TextFillColorSecondaryBrush}"/>
</StackPanel>

<RadioButton x:Name="AccentAutoRadio" Content="Automatic (Windows Accent Color)"
             GroupName="AccentMode" IsChecked="True"
             Checked="AccentModeChanged" Margin="0,0,0,6"/>
<RadioButton x:Name="AccentCustomRadio" Content="Custom Accent Color"
             GroupName="AccentMode"
             Checked="AccentModeChanged" Margin="0,0,0,12"/>

<!-- Custom Color Picker Panel (visible only when Custom is selected) -->
<StackPanel x:Name="CustomAccentPanel" Visibility="Collapsed" Margin="12,0,0,0">
    <!-- Preset Color Swatches -->
    <TextBlock Text="Preset Colors" FontSize="12"
               Foreground="{DynamicResource TextFillColorSecondaryBrush}"
               Margin="0,0,0,6"/>
    <WrapPanel x:Name="ColorSwatchPanel" Margin="0,0,0,12">
        <!-- 16 preset swatches generated in code-behind -->
    </WrapPanel>

    <!-- Hex Input -->
    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
        <TextBlock Text="Custom Hex:" VerticalAlignment="Center"
                   FontSize="12" Margin="0,0,8,0"/>
        <ui:TextBox x:Name="HexColorInput" Width="120"
                    PlaceholderText="#0078D4" MaxLength="7"
                    KeyDown="HexColorInput_KeyDown"/>
        <ui:Button x:Name="BtnApplyHex" Content="Apply"
                   Appearance="Primary" Margin="8,0,0,0"
                   Click="ApplyHexColor_Click"/>
    </StackPanel>

    <!-- Reset -->
    <ui:Button Content="Reset to Windows Accent"
               Icon="{ui:SymbolIcon ArrowReset24}"
               Appearance="Secondary"
               Click="ResetAccentColor_Click"/>
</StackPanel>
```

**Preset colors** (16 swatches matching Windows personalization palette + additional variety):

| Color | Hex | Color | Hex |
|-------|-----|-------|-----|
| Blue | `#0078D4` | Dark Blue | `#003E92` |
| Teal | `#008272` | Cyan | `#0099BC` |
| Green | `#107C10` | Emerald | `#10893E` |
| Yellow | `#986F0B` | Orange | `#CA5010` |
| Red | `#D13438` | Rose | `#E3008C` |
| Purple | `#744DA9` | Violet | `#B146C2` |
| Slate | `#647687` | Steel | `#525E7D` |
| Gold | `#C19C00` | Coral | `#E74856` |

Each swatch is a 28×28 `Border` with `CornerRadius="14"`, a `MouseLeftButtonDown` handler, and a subtle selection ring for the active color.

#### [MODIFY] [AppSettingsPage.xaml.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Setup/AppSettingsPage.xaml.cs)

1. **Inject `AccentColorService`** into the constructor.
2. **`InitializeAccentColorSection()`** — called from constructor:
   - Reads `AppSettings.AccentColorMode` and `CustomAccentColor`.
   - Sets the correct radio button.
   - Generates 16 preset swatch `Border` elements in `ColorSwatchPanel`.
   - Updates `AccentPreviewSwatch` with the current accent color.
   - Shows/hides `CustomAccentPanel` based on mode.
3. **`AccentModeChanged()`** — radio button handler:
   - If "Automatic": calls `_accentColorService.ApplySystemAccent()`, saves settings, hides custom panel.
   - If "Custom": shows custom panel, applies last custom color (or first preset if none saved).
4. **`ColorSwatch_Click(Border swatch)`** — preset click handler:
   - Extracts `Color` from swatch's `Background`.
   - Calls `_accentColorService.ApplyCustomAccent(color)` for live preview.
   - Updates `HexColorInput.Text` with the hex value.
   - Saves to `AppSettings.CustomAccentColor` via `SettingsManager.Save()`.
   - Updates selection ring on swatches.
5. **`ApplyHexColor_Click()`** — validates hex input, calls `ApplyCustomAccent()`, saves.
6. **`HexColorInput_KeyDown()`** — applies on Enter key press.
7. **`ResetAccentColor_Click()`** — sets `AccentAutoRadio.IsChecked = true`, triggering `AccentModeChanged`.
8. **`UpdateAccentPreview(Color color)`** — updates the preview swatch border.

---

### Hardcoded Accent Cleanup

#### [MODIFY] [AnimatedNavIndicatorBehavior.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Helpers/AnimatedNavIndicatorBehavior.cs)

The nav indicator currently reads `NavigationViewSelectionIndicatorForeground` resource at construction time (line 100-101). This works because `ApplicationAccentColorManager.Apply()` updates WPF UI's theme resources which include `NavigationViewSelectionIndicatorForeground`.

**No code change needed** — the WPF UI resource `NavigationViewSelectionIndicatorForeground` is updated automatically by `ApplicationAccentColorManager.Apply()`. However, the indicator `Border` is created once and uses the brush reference. Since the resource returns a `SolidColorBrush` that is replaced (not mutated), we need to:

- Subscribe to `AccentColorService.AccentChanged` and update the indicator's `Background` when the accent changes at runtime.
- Add a method `UpdateIndicatorBrush()` that re-reads the resource and applies it.

#### [MODIFY] [MarkdownConfig.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Infrastructure/MarkdownConfig.cs)

Replace the hardcoded `accent-color: #34D399` with a parameter:
- Add an `accentColor` parameter to the CSS template method.
- When generating CSS, read the current accent from `ApplicationAccentColorManager.SystemAccent` or pass it from the caller.
- The `MarkdownConfig` is used in `NativeMarkdownViewer`, which already reads theme state — extend it to also read accent color.

---

### Dead Code Cleanup

#### [MODIFY] [ShellVisualService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Shell/ShellVisualService.cs)

| Line | Dead Code | Action |
|------|-----------|--------|
| 139 (method signature) | `ApplyTheme(string theme = "Dark")` — `theme` parameter never used | Remove parameter |
| 146 | `SystemThemeWatcher.UnWatch(window)` — `Watch()` is never called | Remove line |

#### [MODIFY] [IShellVisualService.cs](file:///d:/Projects/PocketMC/pocket-mc-windows/PocketMC.Desktop/Features/Shell/Interfaces/IShellVisualService.cs)

| Line | Dead Code | Action |
|------|-----------|--------|
| 10 | `ApplyTheme(string theme = "Dark")` — matches dead parameter | Remove parameter |

---

## User Review Required

> [!IMPORTANT]
> **Accent Color Persistence Scope**
>
> The custom accent color will be stored per-machine in `%LocalAppData%\PocketMC\settings.json` alongside the existing `WindowBackdrop` setting. It is NOT per-instance or per-server — it's a global app appearance preference. The DPAPI encryption applied to secrets does NOT apply to the accent color (it's not sensitive data).

> [!NOTE]
> **Color Picker Approach**
>
> Rather than importing a third-party color picker NuGet, this plan uses a grid of 16 preset color swatches + a hex text input. This matches the Windows Settings personalization pattern and avoids adding new dependencies. If you'd prefer a full HSV/HSL color wheel picker, let me know and I'll evaluate lightweight options.

---

## Open Questions

> [!IMPORTANT]
> **1. Should accent color affect the update banner?** YES
>
> The update banner in `MainWindow.xaml` uses hardcoded `Background="#2563EB"` (line 66). Should this be converted to use the accent color, or remain a fixed informational blue?

> [!IMPORTANT]
> **2. Markdown checkbox accent** NOT NEEDED
>
> The `MarkdownConfig.cs` CSS uses `accent-color: #34D399` (emerald green) for HTML checkboxes in the AI summary viewer. Should this follow the user's accent color, or remain a fixed "premium emerald" design choice?

---

## Verification Plan

### Automated Tests
```bash
dotnet test
```
Ensure all 627 existing tests pass with no regressions.

### Manual Verification

1. **Default behavior unchanged**: Fresh install → accent matches Windows system accent color. Toggle Windows accent in Settings → PocketMC updates immediately.
2. **Custom accent application**: Select "Custom" → click a preset swatch → verify all controls update immediately:
   - Navigation sidebar indicator
   - Toggle switches
   - Progress bars
   - Buttons with `Appearance="Primary"`
   - Focus rings
   - Hyperlinks
   - Selection highlights
3. **Hex input**: Enter `#E3008C` → click Apply → verify pink accent applied.
4. **Invalid hex handling**: Enter `ZZZZZ` → click Apply → nothing crashes, error feedback shown.
5. **Theme switching with custom accent**: Set custom accent → switch from "Solid Dark" to "Solid Light" → verify accent persists and variants recalculate correctly for the light theme.
6. **Persistence**: Set custom orange accent → close app → relaunch → verify orange accent is restored from settings.
7. **Reset**: Click "Reset to Windows Accent" → verify accent reverts to Windows system color.
8. **Settings file**: Verify `settings.json` contains `"AccentColorMode": "Custom"` and `"CustomAccentColor": "#CA5010"` after saving.
