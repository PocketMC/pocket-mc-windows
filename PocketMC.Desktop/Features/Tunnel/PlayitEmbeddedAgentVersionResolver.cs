using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace PocketMC.Desktop.Features.Tunnel;

public static class PlayitEmbeddedAgentVersionResolver
{
    public static PlayitPartnerAgentVersion Resolve(string executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (versionInfo.FileMajorPart > 0 || versionInfo.FileMinorPart > 0 || versionInfo.FileBuildPart > 0)
            {
                return new PlayitPartnerAgentVersion
                {
                    VersionMajor = Math.Max(0, versionInfo.FileMajorPart),
                    VersionMinor = Math.Max(0, versionInfo.FileMinorPart),
                    VersionPatch = Math.Max(0, versionInfo.FileBuildPart)
                };
            }
        }

        Version fallback = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return new PlayitPartnerAgentVersion
        {
            VersionMajor = fallback.Major,
            VersionMinor = Math.Max(0, fallback.Minor),
            VersionPatch = Math.Max(0, fallback.Build)
        };
    }
}
