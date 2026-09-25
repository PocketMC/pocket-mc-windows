using PocketMC.Desktop.Tests.TestSupport.Utilities;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Setup.Architecture;

public sealed class WallpaperBlurConfigurationTests
{
    [Fact]
    public void AppSettings_Defaults_IncludeWallpaperBlurRadiusAndTintOpacity()
    {
        var settings = new AppSettings();
        Assert.Equal(80.0, settings.WallpaperBlurRadius);
        Assert.Equal(0.72, settings.WallpaperTintOpacity);
    }

    [Fact]
    public void Xaml_DefinesWallpaperBlurAndDimmingControls()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml"));

        Assert.Contains("Blur Intensity", xaml);
        Assert.Contains("Darkness &amp; Dimming", xaml);
        Assert.Contains("x:Name=\"WallpaperBlurSlider\"", xaml);
        Assert.Contains("x:Name=\"WallpaperBlurValueText\"", xaml);
        Assert.Contains("x:Name=\"WallpaperTintSlider\"", xaml);
        Assert.Contains("x:Name=\"WallpaperTintValueText\"", xaml);
        Assert.Contains("x:Name=\"BtnResetWallpaperEffects\"", xaml);
        Assert.Contains("ValueChanged=\"WallpaperBlurSlider_ValueChanged\"", xaml);
        Assert.Contains("ValueChanged=\"WallpaperTintSlider_ValueChanged\"", xaml);
        Assert.Contains("Click=\"ResetWallpaperEffects_Click\"", xaml);
    }

    [Fact]
    public void CodeBehind_ContainsWallpaperBlurAndDimmingHandlers()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Setup",
            "AppSettingsPage.xaml.cs"));

        Assert.Contains("WallpaperBlurSlider_ValueChanged", source);
        Assert.Contains("WallpaperTintSlider_ValueChanged", source);
        Assert.Contains("ResetWallpaperEffects_Click", source);
        Assert.Contains("InitializeWallpaperEffects", source);
        Assert.Contains("FormatBlurText", source);
        Assert.Contains("WallpaperMicaService.UpdateTintOverlay", source);
    }

    [Fact]
    public void WallpaperMicaService_SupportsBlurRadiusAndTintOpacity()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "WallpaperMicaService.cs"));

        Assert.Contains("double blurRadius = 80.0", source);
        Assert.Contains("double tintOpacity = 0.72", source);
        Assert.Contains("UpdateTintOverlay", source);
        Assert.Contains("CreatePreBlurredBitmap", source);
    }
}
