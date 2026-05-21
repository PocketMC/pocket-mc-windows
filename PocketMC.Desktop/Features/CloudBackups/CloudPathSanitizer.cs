using System;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.CloudBackups;

public static class CloudPathSanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex InvalidCharsRegex = new(
        @"[<>:""/\\|?*]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    
    public static string SanitizeFolderName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Unknown";
        string sanitized = InvalidCharsRegex.Replace(input, "-");
        return sanitized.Trim();
    }
}
