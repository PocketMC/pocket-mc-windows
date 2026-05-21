using System;
using System.Collections.Generic;
using System.IO;

namespace PocketMC.Desktop.Features.Marketplace;

public static class MarketplaceFileNameSanitizer
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public static string RequireSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Marketplace file name is missing.");
        }

        string normalized = fileName.Replace('\\', '/');
        int slashIndex = normalized.LastIndexOf('/');
        string leafName = slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
        string trimmedLeafName = leafName.Trim();

        if (string.IsNullOrWhiteSpace(leafName) ||
            trimmedLeafName.Length != leafName.Length ||
            leafName.EndsWith(".", StringComparison.Ordinal) ||
            leafName.Contains(':', StringComparison.Ordinal) ||
            leafName == "." ||
            leafName == ".." ||
            leafName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("Marketplace file name is invalid.");
        }

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(leafName);
        if (ReservedWindowsNames.Contains(nameWithoutExtension))
        {
            throw new InvalidOperationException("Marketplace file name uses a reserved Windows device name.");
        }

        return leafName;
    }
}
