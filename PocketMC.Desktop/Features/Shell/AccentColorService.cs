using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PocketMC.Desktop.Core.Interfaces;
using Wpf.Ui.Appearance;

namespace PocketMC.Desktop.Features.Shell;

public sealed class AccentColorService : IDisposable
{
    public const string AutomaticMode = "Automatic";
    public const string CustomMode = "Custom";
    public const string DefaultCustomAccentColor = "#0078D4";

    private static readonly Color FallbackSystemAccent = Color.FromRgb(0x00, 0x78, 0xD4);
    private readonly ApplicationState _applicationState;
    private bool _disposed;

    public event Action<Color>? AccentChanged;
    public static event Action<Color>? GlobalAccentChanged;

    public AccentColorService(ApplicationState applicationState)
    {
        _applicationState = applicationState;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void ApplyCurrentAccent()
    {
        var settings = _applicationState.Settings;
        if (IsCustomMode(settings.AccentColorMode) &&
            TryParseHexColor(settings.CustomAccentColor, out Color customColor, out _))
        {
            ApplyCustomAccent(customColor);
            return;
        }

        ApplySystemAccent();
    }

    public void ApplyCustomAccent(Color color)
    {
        ApplicationAccentColorManager.Apply(color, GetCurrentTheme(), false);
        NotifyAccentChanged(color);
    }

    public void ApplySystemAccent()
    {
        Color systemColor = GetSystemAccentColor();
        ApplicationAccentColorManager.Apply(systemColor, GetCurrentTheme(), false);
        NotifyAccentChanged(systemColor);
    }

    /// <summary>
    /// Re-applies the current accent to application resources.
    /// Call this after any operation that may have reset the accent resources
    /// (e.g., new FluentWindow initialization, theme dictionary reload).
    /// </summary>
    public void ReassertAccent()
    {
        var settings = _applicationState.Settings;
        if (IsCustomMode(settings.AccentColorMode) &&
            TryParseHexColor(settings.CustomAccentColor, out Color customColor, out _))
        {
            ApplicationAccentColorManager.Apply(customColor, GetCurrentTheme(), false);
            return;
        }

        Color systemColor = GetSystemAccentColor();
        ApplicationAccentColorManager.Apply(systemColor, GetCurrentTheme(), false);
    }

    public Color GetCurrentAccentColor()
    {
        var settings = _applicationState.Settings;
        if (IsCustomMode(settings.AccentColorMode) &&
            TryParseHexColor(settings.CustomAccentColor, out Color customColor, out _))
        {
            return customColor;
        }

        return GetSystemAccentColor();
    }

    public static bool IsCustomMode(string? mode) =>
        string.Equals(mode, CustomMode, StringComparison.OrdinalIgnoreCase);

    public static bool TryParseHexColor(string? value, out Color color, out string normalizedHex)
    {
        color = default;
        normalizedHex = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string hex = value.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6 || hex.Any(c => !Uri.IsHexDigit(c)))
        {
            return false;
        }

        byte red = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte green = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte blue = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        color = Color.FromRgb(red, green, blue);
        normalizedHex = "#" + hex.ToUpperInvariant();
        return true;
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static ApplicationTheme GetCurrentTheme() =>
        ApplicationThemeManager.GetAppTheme();

    private static Color GetSystemAccentColor()
    {
        try
        {
            return ApplicationAccentColorManager.GetColorizationColor();
        }
        catch
        {
            try
            {
                return ApplicationAccentColorManager.SystemAccent;
            }
            catch
            {
                return FallbackSystemAccent;
            }
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.Color &&
            e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        if (IsCustomMode(_applicationState.Settings.AccentColorMode))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher?.CheckAccess() == false)
        {
            dispatcher.BeginInvoke(ApplyCurrentAccent);
        }
        else
        {
            ApplyCurrentAccent();
        }
    }

    private void NotifyAccentChanged(Color color)
    {
        AccentChanged?.Invoke(color);
        GlobalAccentChanged?.Invoke(color);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _disposed = true;
    }
}
