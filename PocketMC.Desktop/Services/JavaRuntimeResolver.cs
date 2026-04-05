using System.IO;
using System.Text.RegularExpressions;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services;

public static class JavaRuntimeResolver
{
    private static readonly int[] BundledJavaVersions = { 8, 17, 21, 25 };
    private static readonly Regex LeadingVersionRegex = new(
        @"^\d+(?:\.\d+){0,2}",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public static IReadOnlyList<int> GetBundledJavaVersions() => BundledJavaVersions;

    public static int GetRequiredJavaVersion(string? minecraftVersion)
    {
        if (!TryParseVersion(minecraftVersion, out var version))
        {
            return 21;
        }

        if (IsVersionInRange(version, "1.0", "1.16.4"))
        {
            return 8;
        }

        if (IsVersionInRange(version, "1.16.5", "1.17.1"))
        {
            return 11;
        }

        if (IsVersionInRange(version, "1.18", "1.20.4"))
        {
            return 17;
        }

        if (IsVersionInRange(version, "1.20.5", "1.21.1"))
        {
            return 21;
        }

        // Minecraft 1.21.2+ (in 2026) requires Java 25
        return 25;
    }

    public static string? GetBundledJavaPath(string appRootPath, int javaVersion)
    {
        string bundledPath = Path.Combine(appRootPath, "runtime", $"java{javaVersion}", "bin", "java.exe");
        return File.Exists(bundledPath) ? bundledPath : null;
    }

    public static string ResolveJavaPath(InstanceMetadata metadata, string appRootPath)
    {
        if (!string.IsNullOrWhiteSpace(metadata.CustomJavaPath) && File.Exists(metadata.CustomJavaPath))
        {
            return metadata.CustomJavaPath!;
        }

        int requiredJavaVersion = GetRequiredJavaVersion(metadata.MinecraftVersion);
        return GetBundledJavaPath(appRootPath, requiredJavaVersion) ?? "java";
    }

    private static bool IsVersionInRange(Version version, string minInclusive, string maxInclusive)
    {
        return version >= ParseVersion(minInclusive) && version <= ParseVersion(maxInclusive);
    }

    public static bool TryParseVersion(string? rawVersion, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var match = LeadingVersionRegex.Match(rawVersion.Trim());
        if (!match.Success)
        {
            return false;
        }

        try
        {
            version = ParseVersion(match.Value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static Version ParseVersion(string versionText)
    {
        var parts = versionText.Split('.');
        int major = ParsePart(parts, 0);
        int minor = ParsePart(parts, 1);
        int patch = ParsePart(parts, 2);
        return new Version(major, minor, patch);
    }

    private static int ParsePart(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return 0;
        }

        return int.Parse(parts[index]);
    }
}
