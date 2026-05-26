using System.IO;

namespace PocketMC.Desktop.Features.Mods;

public static class AddonFileNamePolicy
{
    public const string DisabledSuffix = ".disabled-by-pocketmc";
    private const string JarExtension = ".jar";

    public static bool IsEnabledJarFileName(string fileName)
    {
        return IsSafeFileName(fileName) &&
               fileName.EndsWith(JarExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDisabledJarFileName(string fileName)
    {
        return IsSafeFileName(fileName) &&
               fileName.EndsWith(JarExtension + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDisabledFileName(string originalFileName)
    {
        if (!IsEnabledJarFileName(originalFileName))
        {
            throw new ArgumentException("Add-on file name must be a safe .jar file name.", nameof(originalFileName));
        }

        return originalFileName + DisabledSuffix;
    }

    public static string GetOriginalFileNameFromDisabled(string disabledFileName)
    {
        if (!IsDisabledJarFileName(disabledFileName))
        {
            throw new ArgumentException("Disabled add-on file name is not managed by PocketMC.", nameof(disabledFileName));
        }

        return disabledFileName[..^DisabledSuffix.Length];
    }

    public static string GetUniqueDisabledFileName(string originalFileName, string disabledDirectory)
    {
        string baseName = Path.GetFileNameWithoutExtension(originalFileName);
        string extension = Path.GetExtension(originalFileName);
        string candidate = GetDisabledFileName(originalFileName);
        int suffix = 1;

        while (File.Exists(Path.Combine(disabledDirectory, candidate)))
        {
            candidate = $"{baseName} ({suffix}){extension}{DisabledSuffix}";
            suffix++;
        }

        return candidate;
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    public static string KindDirectory(AddonKind kind)
    {
        return kind == AddonKind.Plugin ? "plugins" : "mods";
    }

    private static bool IsSafeFileName(string fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) &&
               Path.GetFileName(fileName).Equals(fileName, StringComparison.Ordinal) &&
               fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
