using System.IO;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Desktop.Features.Mods;

public sealed class ModpackOverrideExtractionResult
{
    public int ExtractedOverrideCount { get; set; }
    public int SkippedOverrideCount => SkippedOverrides.Count;
    public List<ModpackSkippedOverride> SkippedOverrides { get; } = new();
}

public sealed record ModpackSkippedOverride(string Path, string Reason);

public static class ModpackOverridePolicy
{
    private static readonly HashSet<string> AllowedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "config",
        "defaultconfigs",
        "kubejs",
        "scripts",
        "datapacks",
        "resourcepacks",
        "shaderpacks",
        "mods"
    };

    private static readonly HashSet<string> BlockedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "libraries",
        "runtime",
        "runtimes",
        "tunnel",
        "backups",
        ".pocketmc-updates"
    };

    private static readonly HashSet<string> BlockedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "server.jar",
        "installer.jar",
        "forge-installer.jar",
        "PocketMine-MP.phar",
        "bedrock_server.exe",
        ".pocket-mc.json",
        "addon_manifest.json",
        "server.properties",
        "eula.txt",
        "ops.json",
        "whitelist.json",
        "allowlist.json",
        "permissions.json"
    };

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".dll",
        ".bat",
        ".cmd",
        ".ps1",
        ".sh"
    };

    private static readonly HashSet<string> AllowedModsExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jar"
    };

    public static bool TryValidate(string relativePath, out string normalizedPath, out string reason)
    {
        normalizedPath = NormalizePath(relativePath);
        reason = "";

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            reason = "Override path is empty.";
            return false;
        }

        if (PathSafety.ContainsTraversal(normalizedPath) || Path.IsPathRooted(normalizedPath))
        {
            reason = "Override path contains path traversal.";
            return false;
        }

        string[] parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            reason = "Override path is empty.";
            return false;
        }

        string root = parts[0];
        string fileName = parts[^1];
        string extension = Path.GetExtension(fileName);

        if (BlockedRootDirectories.Contains(root))
        {
            reason = $"Override root '{root}' is protected.";
            return false;
        }

        if (BlockedFileNames.Contains(fileName) || BlockedFileNames.Contains(normalizedPath))
        {
            reason = $"Override file '{fileName}' is protected.";
            return false;
        }

        if (BlockedExtensions.Contains(extension))
        {
            reason = $"Override extension '{extension}' is not allowed.";
            return false;
        }

        if (!AllowedRootDirectories.Contains(root))
        {
            reason = $"Override root '{root}' is not allowed.";
            return false;
        }

        if (root.Equals("mods", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(fileName) &&
            !AllowedModsExtensions.Contains(extension))
        {
            reason = $"Override mod file extension '{extension}' is not allowed.";
            return false;
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
