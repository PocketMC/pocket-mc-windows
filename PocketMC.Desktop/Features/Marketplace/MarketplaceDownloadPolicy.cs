using System.IO;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Marketplace;

public static class MarketplaceDownloadPolicy
{
    public static string RequireCompatibleFileName(string fileName, EngineCompatibility compatibility, bool isModpack = false)
    {
        string safeFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(fileName);
        string extension = Path.GetExtension(safeFileName);

        if (isModpack)
        {
            RequireExtension(safeFileName, extension, ".zip", ".mrpack");
            return safeFileName;
        }

        switch (compatibility.Family)
        {
            case EngineFamily.Bedrock:
                RequireExtension(safeFileName, extension, ".mcpack", ".mcaddon", ".zip");
                break;
            case EngineFamily.Pocketmine:
                RequireExtension(safeFileName, extension, ".phar");
                break;
            default:
                RequireExtension(safeFileName, extension, ".jar");
                break;
        }

        return safeFileName;
    }

    private static void RequireExtension(string fileName, string extension, params string[] allowedExtensions)
    {
        if (!allowedExtensions.Any(allowed => extension.Equals(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException(
                $"Marketplace file '{fileName}' has unsupported extension '{extension}'. Allowed extensions: {string.Join(", ", allowedExtensions)}.");
        }
    }
}
