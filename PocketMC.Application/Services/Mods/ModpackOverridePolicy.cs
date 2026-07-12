using System.IO;
using PocketMC.Domain.Security;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Mods;

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

    private static readonly HashSet<string> AllowedScriptsExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zs",
        ".js",
        ".json"
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

        // We MUST retain path traversal checks to prevent malicious modpacks from escaping the instance folder.
        if (PathSafety.ContainsTraversal(normalizedPath) || Path.IsPathRooted(normalizedPath))
        {
            reason = "Override path contains path traversal.";
            return false;
        }

        // Bypass all other safety checks per user request!
        return true;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
