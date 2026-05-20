using System;
using System.IO;

namespace PocketMC.Desktop.Features.Marketplace;

public static class MarketplaceFileNameSanitizer
{
    public static string RequireSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Marketplace file name is missing.");
        }

        string normalized = fileName.Replace('\\', '/');
        int slashIndex = normalized.LastIndexOf('/');
        string leafName = slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;

        if (string.IsNullOrWhiteSpace(leafName) ||
            leafName == "." ||
            leafName == ".." ||
            leafName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("Marketplace file name is invalid.");
        }

        return leafName;
    }
}
