using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Java;

namespace PocketMC.Infrastructure.Instances;

public class ExportManifestBuilder
{
    private const string ServerIconFileName = "server-icon.png";
    private readonly AddonExportService _addonExportService;

    public ExportManifestBuilder(AddonExportService addonExportService)
    {
        _addonExportService = addonExportService;
    }

    public async Task<InstanceExportManifest> BuildManifestAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        bool isJava,
        CancellationToken cancellationToken)
    {
        string iconPath = Path.Combine(instanceRoot, ServerIconFileName);
        var manifest = new InstanceExportManifest
        {
            Origin = new InstanceExportOrigin
            {
                PocketMcVersion = GetPocketMcVersion(),
                Timestamp = DateTimeOffset.UtcNow
            },
            ServerMeta = new InstanceExportServerMeta
            {
                Name = metadata.Name,
                Description = metadata.Description,
                Icon = File.Exists(iconPath) ? "meta/icon.png" : null
            },
            Software = BuildSoftwareManifest(metadata, isJava),
            Runtime = BuildRuntimeManifest(metadata, isJava)
        };

        var addonsList = await _addonExportService.BuildAddonManifestAsync(metadata, instanceRoot, isJava, cancellationToken)
            .ConfigureAwait(false);
        manifest.Addons.AddRange(addonsList);

        return manifest;
    }

    private static ServerSoftwareManifest BuildSoftwareManifest(InstanceMetadata metadata, bool isJava)
    {
        ServerSoftwareManifest software = isJava
            ? new JavaServerSoftwareManifest()
            : new BedrockServerSoftwareManifest();

        software.Type = NormalizeServerType(metadata.ServerType, isJava);
        software.MinecraftVersion = metadata.MinecraftVersion;
        software.LoaderVersion = string.IsNullOrWhiteSpace(metadata.LoaderVersion) ? null : metadata.LoaderVersion;
        return software;
    }

    private static InstanceRuntimeManifest BuildRuntimeManifest(InstanceMetadata metadata, bool isJava)
    {
        if (!isJava)
        {
            return new InstanceRuntimeManifest { Type = InstanceRuntimeType.Native };
        }

        int javaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(metadata);
        return new InstanceRuntimeManifest
        {
            Type = InstanceRuntimeType.Java,
            TargetVersion = javaVersion.ToString()
        };
    }

    private static string NormalizeServerType(string? serverType, bool isJava)
    {
        if (string.IsNullOrWhiteSpace(serverType))
        {
            return isJava ? "Vanilla" : "BDS";
        }

        if (!isJava && serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            return "BDS";
        }

        return serverType.Trim();
    }

    private static string GetPocketMcVersion()
    {
        Assembly assembly = typeof(InstanceExportService).Assembly;
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }
}
