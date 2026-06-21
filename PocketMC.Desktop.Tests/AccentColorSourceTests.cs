namespace PocketMC.Desktop.Tests;

public sealed class AccentColorSourceTests
{
    [Fact]
    public void ShellVisualService_ReappliesAccentAfterEveryThemeApply()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "ShellVisualService.cs"));

        Assert.Contains("AccentColorService", source);
        Assert.Contains("_accentColorService.ApplyCurrentAccent();", source);
        Assert.DoesNotContain("ApplyTheme(string theme", source);
        Assert.DoesNotContain("SystemThemeWatcher.UnWatch", source);
        Assert.Contains("updateAccent: false", source);

        int themeApply = source.IndexOf("ApplicationThemeManager.Apply", StringComparison.Ordinal);
        int accentApply = source.IndexOf("_accentColorService.ApplyCurrentAccent();", StringComparison.Ordinal);
        Assert.True(themeApply >= 0);
        Assert.True(accentApply > themeApply);
    }

    [Fact]
    public void AccentColorService_ExposesReassertAccentMethod()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "AccentColorService.cs"));

        Assert.Contains("public void ReassertAccent()", source);
        Assert.Contains("ApplicationAccentColorManager.Apply(", source);
    }

    [Fact]
    public void AppDialogWindow_ReassertAccentAfterInitialization()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Infrastructure",
            "AppDialogWindow.xaml.cs"));

        Assert.Contains("ReassertAccent", source);
    }

    [Fact]
    public void MainWindow_WindowLoaded_NoRedundantThemeApply()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "MainWindow.xaml.cs"));

        // Window_Loaded should call RequestMicaUpdate (which internally calls ApplyTheme)
        // but should not call ApplyTheme directly — that's a redundant double-apply
        var windowLoadedStart = source.IndexOf("Window_Loaded", StringComparison.Ordinal);
        var windowLoadedBody = source.Substring(windowLoadedStart, 300);
        Assert.DoesNotContain("_visualService.ApplyTheme()", windowLoadedBody);
    }

    [Fact]
    public void PresentationLayer_RegistersAccentColorServiceSingleton()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Composition",
            "ServiceCollectionExtensions.cs"));

        Assert.Contains("services.AddSingleton<AccentColorService>();", source);
        Assert.Contains("services.AddSingleton<IShellVisualService, ShellVisualService>();", source);
    }

    [Fact]
    public void AppSettingsPageXaml_DefinesAccentColorControlsInsideAppearance()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml"));

        Assert.Contains("Accent Color", xaml);
        Assert.Contains("x:Name=\"AccentPreviewSwatch\"", xaml);
        Assert.Contains("x:Name=\"AccentModeLabel\"", xaml);
        Assert.Contains("x:Name=\"AccentAutoRadio\"", xaml);
        Assert.Contains("Automatic (Windows Accent Color)", xaml);
        Assert.Contains("x:Name=\"AccentCustomRadio\"", xaml);
        Assert.Contains("x:Name=\"CustomAccentPanel\"", xaml);
        Assert.Contains("x:Name=\"ColorSwatchPanel\"", xaml);
        Assert.Contains("x:Name=\"HexColorInput\"", xaml);
        Assert.Contains("Reset to Windows Accent", xaml);
    }

    [Fact]
    public void AppSettingsPageCodeBehind_SavesAndAppliesAccentChanges()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml.cs"));

        Assert.Contains("AccentColorService", source);
        Assert.Contains("InitializeAccentColorSection", source);
        Assert.Contains("AccentModeChanged", source);
        Assert.Contains("ColorSwatch_Click", source);
        Assert.Contains("ApplyHexColor_Click", source);
        Assert.Contains("HexColorInput_KeyDown", source);
        Assert.Contains("ResetAccentColor_Click", source);
        Assert.Contains("_accentColorService.ApplyCustomAccent", source);
        Assert.Contains("_accentColorService.ApplySystemAccent", source);
        Assert.Contains("settings.CustomAccentColor", source);
        Assert.Contains("_settingsManager.Save(settings)", source);
    }

    [Fact]
    public void MainWindowUpdateBanner_UsesAccentResourceInsteadOfFixedBlue()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "MainWindow.xaml"));

        Assert.DoesNotContain("Background=\"#2563EB\"", xaml);
        Assert.Contains("Background=\"{DynamicResource SystemAccentColorPrimaryBrush}\"", xaml);
    }

    [Fact]
    public void AnimatedNavIndicator_ReReadsBrushWhenAccentChanges()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Helpers",
            "AnimatedNavIndicatorBehavior.cs"));

        Assert.Contains("AccentColorService.GlobalAccentChanged", source);
        Assert.Contains("UpdateIndicatorBrush", source);
        Assert.Contains("NavigationViewSelectionIndicatorForeground", source);
    }
}
