using System.Globalization;

namespace PocketMC.Desktop.Localization;

/// <summary>Short aliases for translating C# strings: <c>L.T("Online")</c>, <c>L.F("Delete {0}?", name)</c>.</summary>
public static class L
{
    public static string T(string source) => LocalizationService.Instance.Get(source);

    public static string F(string source, params object?[] args) => LocalizationService.Instance.Format(source, args);

    public static string Date(DateTime value) => LocalizationService.Instance.FormatDate(value);

    public static string DateTime(DateTime value) => LocalizationService.Instance.FormatDateTime(value);

    public static CultureInfo Culture => LocalizationService.Instance.Culture;
}
