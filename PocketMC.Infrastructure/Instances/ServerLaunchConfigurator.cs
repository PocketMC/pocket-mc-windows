using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Instances.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Infrastructure.Instances;

/// <summary>
/// Encapsulates the logic for configuring a Minecraft server process launch.
/// Delegates the specific configuration logic to engine-specific configurators.
/// </summary>
public class ServerLaunchConfigurator
{
    private readonly JavaLaunchConfigurator _javaConfigurator;
    private readonly BedrockLaunchConfigurator _bedrockConfigurator;
    private readonly PocketMineLaunchConfigurator _pocketmineConfigurator;

    internal Func<int, string, Task<bool>> ConfirmJavaDownloadPrompt
    {
        get => _javaConfigurator.ConfirmJavaDownloadPrompt;
        set => _javaConfigurator.ConfirmJavaDownloadPrompt = value;
    }

    public ServerLaunchConfigurator(
        JavaLaunchConfigurator javaConfigurator,
        BedrockLaunchConfigurator bedrockConfigurator,
        PocketMineLaunchConfigurator pocketmineConfigurator)
    {
        _javaConfigurator = javaConfigurator;
        _bedrockConfigurator = bedrockConfigurator;
        _pocketmineConfigurator = pocketmineConfigurator;
    }

    /// <summary>
    /// Legacy constructor for backward compatibility and tests.
    /// </summary>
    internal ServerLaunchConfigurator(
        JavaProvisioningService javaProvisioning,
        PhpProvisioningService phpProvisioning,
        VanillaProvider vanillaProvider,
        ILogger<ServerLaunchConfigurator> logger)
    {
        var forgeInstaller = new ForgeInstaller(vanillaProvider);
        _javaConfigurator = new JavaLaunchConfigurator(javaProvisioning, forgeInstaller, NullLogger<JavaLaunchConfigurator>.Instance);
        _bedrockConfigurator = new BedrockLaunchConfigurator();
        _pocketmineConfigurator = new PocketMineLaunchConfigurator(phpProvisioning, NullLogger<PocketMineLaunchConfigurator>.Instance);
    }

    public async Task<ProcessStartInfo> ConfigureAsync(
        InstanceMetadata meta,
        string workingDir,
        string appRootPath,
        Action<string> onLog,
        Action<ServerState>? onStateChange = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
        }

        if (meta.ServerType != null && meta.ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            return _bedrockConfigurator.Configure(meta, workingDir, onLog);
        }

        if (meta.ServerType != null && meta.ServerType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
        {
            return await _pocketmineConfigurator.ConfigureAsync(meta, workingDir, appRootPath, onLog);
        }

        // Java servers
        return await _javaConfigurator.ConfigureAsync(meta, workingDir, appRootPath, onLog, onStateChange, cancellationToken);
    }
}
