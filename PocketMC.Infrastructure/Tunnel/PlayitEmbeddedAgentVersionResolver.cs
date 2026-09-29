using System;
using System.IO;

namespace PocketMC.Infrastructure.Tunnel;

public static class PlayitEmbeddedAgentVersionResolver
{
    public static PlayitPartnerAgentVersion Resolve(string executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            Version? version = PlayitRuntimeManifest.GetExecutableVersion(executablePath);
            if (version != null)
            {
                return new PlayitPartnerAgentVersion
                {
                    VersionMajor = Math.Max(0, version.Major),
                    VersionMinor = Math.Max(0, version.Minor),
                    VersionPatch = Math.Max(0, version.Build)
                };
            }
        }

        return new PlayitPartnerAgentVersion
        {
            VersionMajor = PlayitRuntimeManifest.MinimumSupportedVersion.Major,
            VersionMinor = PlayitRuntimeManifest.MinimumSupportedVersion.Minor,
            VersionPatch = PlayitRuntimeManifest.MinimumSupportedVersion.Build
        };
    }
}
