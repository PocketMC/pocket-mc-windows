using System;
using System.Diagnostics;
using System.IO;

namespace PocketMC.Infrastructure.Tunnel;

public static class PlayitRuntimeManifest
{
    public static Version MinimumSupportedVersion { get; } = new(1, 0, 10);
    public static Version? MaximumSupportedVersion { get; } = null;
    public static string TargetVersion => MinimumSupportedVersion.ToString(3);
    public static string Platform => "windows-x64";
    public static string ExecutableName => "playit.exe";
    public static string DownloadUrl => $"https://github.com/playit-cloud/playit-agent/releases/download/v{TargetVersion}/playit-windows-x86_64-signed.exe";
    public const string ExpectedSha256 = "2dbdaad119844cbbc062cc9774b8b462afa5f1b4b7832a9fc5ef4676cae887cf";

    public static bool IsCompatibleVersion(Version version)
        => version >= MinimumSupportedVersion &&
           (MaximumSupportedVersion == null || version <= MaximumSupportedVersion);

    public static Version? GetExecutableVersion(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
        foreach (string? candidate in new[] { versionInfo.ProductVersion, versionInfo.FileVersion })
        {
            if (TryParseVersion(candidate, out Version? version))
            {
                return version;
            }
        }

        if (versionInfo.FileMajorPart > 0 || versionInfo.FileMinorPart > 0 || versionInfo.FileBuildPart > 0)
        {
            return new Version(
                Math.Max(0, versionInfo.FileMajorPart),
                Math.Max(0, versionInfo.FileMinorPart),
                Math.Max(0, versionInfo.FileBuildPart));
        }

        return null;
    }

    private static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().TrimStart('v', 'V');
        int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out version);
    }
}