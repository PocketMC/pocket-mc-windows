using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Networking;

/// <summary>
/// Performs side-effect-free validation of an instance's configured port setup before startup.
/// </summary>
public sealed class PortPreflightService
{
    private const int MinValidPort = 1;
    private const int MaxValidPort = 65535;
    private const int MaxPrivilegedPort = 1023;
    private const int DefaultJavaPort = 25565;
    private const int DefaultBedrockPort = 19132;

    private readonly InstanceRegistry _registry;
    private readonly ServerConfigurationService _configurationService;
    private readonly ServerProcessManager _serverProcessManager;
    private readonly PocketMC.Desktop.Features.Shell.ApplicationState _applicationState;
    private readonly ILogger<PortPreflightService> _logger;

    /// <summary>
    /// Initializes a new port preflight service.
    /// </summary>
    public PortPreflightService(
        InstanceRegistry registry,
        ServerConfigurationService configurationService,
        ServerProcessManager serverProcessManager,
        PocketMC.Desktop.Features.Shell.ApplicationState applicationState,
        ILogger<PortPreflightService> logger)
    {
        _registry = registry;
        _configurationService = configurationService;
        _serverProcessManager = serverProcessManager;
        _applicationState = applicationState;
        _logger = logger;
    }

    /// <summary>
    /// Validates the configured port setup for the supplied instance.
    /// </summary>
    /// <param name="metadata">The instance to validate.</param>
    /// <returns>A structured preflight result for the instance's primary server port setup.</returns>
    public PortCheckResult Check(InstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Check(metadata, _registry.GetPath(metadata.Id));
    }

    /// <summary>
    /// Validates the configured port setup for the supplied instance and server directory.
    /// </summary>
    /// <param name="metadata">The instance to validate.</param>
    /// <param name="serverDir">The instance directory that contains configuration files.</param>
    /// <returns>A structured preflight result for the instance's primary server port setup.</returns>
    public PortCheckResult Check(InstanceMetadata metadata, string? serverDir)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var configuration = LoadConfigurationSnapshot(metadata, serverDir);
        var targets = BuildTargets(metadata, configuration, serverDir);
        var primaryTarget = targets[0];
        var primaryRequest = CreateRequest(metadata, serverDir, primaryTarget);

        var occupiedTargets = GetOtherInstanceTargets(metadata.Id);
        var detectedConflicts = new List<DetectedConflict>();
        var recommendations = new List<PortRecoveryRecommendation>();

        bool isPlayitEnabled = _applicationState.IsConfigured && System.IO.File.Exists(_applicationState.GetPlayitExecutablePath());

        foreach (var target in targets)
        {
            if (!IsPortInValidRange(target.Port))
            {
                recommendations.Add(
                    new PortRecoveryRecommendation(
                        PortFailureCode.InvalidRange,
                        $"Use a valid {target.Name} port",
                        $"{target.Name} uses port {target.Port}, but PocketMC only supports ports from {MinValidPort} to {MaxValidPort}.",
                        suggestedPort: target.DefaultPort,
                        suggestedProtocol: target.Protocol,
                        suggestedIpMode: target.IpMode,
                        canAutoApply: false,
                        requiresUserAction: true));

                return new PortCheckResult(
                    primaryRequest,
                    isSuccessful: false,
                    canBindLocally: false,
                    failureCode: PortFailureCode.InvalidRange,
                    failureMessage: $"{target.Name} port {target.Port} is outside the valid range {MinValidPort}-{MaxValidPort}.",
                    lease: null,
                    conflicts: Array.Empty<PortConflictInfo>(),
                    recommendations: recommendations);
            }

            if (IsPrivilegedPort(target.Port))
            {
                int suggestedPort = FindSuggestedPort(target, occupiedTargets);
                recommendations.Add(
                    new PortRecoveryRecommendation(
                        PortFailureCode.ReservedOrPrivilegedPort,
                        $"Avoid low-numbered {target.Name} ports",
                        $"{target.Name} uses port {target.Port}, which is in the privileged/reserved range. PocketMC can keep this configuration, but a higher port is safer and less likely to clash with system services.",
                        suggestedPort: suggestedPort,
                        suggestedProtocol: target.Protocol,
                        suggestedIpMode: target.IpMode,
                        canAutoApply: false,
                        requiresUserAction: true));
            }

            foreach (var occupied in occupiedTargets)
            {
                if (!TargetsConflict(target, occupied.Target))
                {
                    continue;
                }

                if (!occupied.IsRunning)
                {
                    continue;
                }

                if (isPlayitEnabled)
                {
                    continue;
                }

                string detail = $"Instance '{occupied.Metadata.Name}' is currently running with a matching {FormatProtocol(target.Protocol)} port binding.";

                detectedConflicts.Add(
                    new DetectedConflict(
                        target,
                        new PortConflictInfo(
                            PortFailureCode.InUseByPocketMcInstance,
                            target.Port,
                            target.Protocol,
                            target.IpMode,
                            target.BindAddress,
                            existingLease: new PortLease(
                                occupied.Target.Port,
                                occupied.Target.Protocol,
                                occupied.Target.IpMode,
                                occupied.Metadata.Id,
                                occupied.Metadata.Name,
                                occupied.ServerDir,
                                occupied.Target.BindAddress,
                                acquiredAtUtc: DateTimeOffset.MinValue),
                            processId: null,
                            processName: null,
                            details: detail)));
            }
        }

        if (detectedConflicts.Count > 0)
        {
            var primaryConflict = detectedConflicts[0];
            int suggestedPort = FindSuggestedPort(primaryConflict.Target, occupiedTargets);

            recommendations.Add(
                new PortRecoveryRecommendation(
                    PortFailureCode.InUseByPocketMcInstance,
                    $"Move {primaryConflict.Target.Name} to a free port",
                    $"{primaryConflict.Target.Name} conflicts with another PocketMC instance. Choose a different {FormatProtocol(primaryConflict.Target.Protocol)} port before starting the server.",
                    suggestedPort: suggestedPort,
                    suggestedProtocol: primaryConflict.Target.Protocol,
                    suggestedIpMode: primaryConflict.Target.IpMode,
                    canAutoApply: false,
                    requiresUserAction: true));

            return new PortCheckResult(
                primaryRequest,
                isSuccessful: false,
                canBindLocally: false,
                failureCode: PortFailureCode.InUseByPocketMcInstance,
                failureMessage: BuildConflictMessage(primaryConflict.Target, primaryConflict.Conflict),
                lease: null,
                conflicts: detectedConflicts.Select(x => x.Conflict).ToArray(),
                recommendations: recommendations);
        }

        return new PortCheckResult(
            primaryRequest,
            isSuccessful: true,
            canBindLocally: true,
            failureCode: PortFailureCode.None,
            failureMessage: null,
            lease: null,
            conflicts: Array.Empty<PortConflictInfo>(),
            recommendations: recommendations);
    }

    /// <summary>
    /// Builds the concrete port check requests that this instance requires for startup.
    /// </summary>
    /// <param name="metadata">The instance to inspect.</param>
    /// <returns>The normalized port check requests for the instance.</returns>
    public IReadOnlyList<PortCheckRequest> BuildRequests(InstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return BuildRequests(metadata, _registry.GetPath(metadata.Id));
    }

    /// <summary>
    /// Builds the concrete port check requests that this instance requires for startup.
    /// </summary>
    /// <param name="metadata">The instance to inspect.</param>
    /// <param name="serverDir">The instance directory that contains configuration files.</param>
    /// <returns>The normalized port check requests for the instance.</returns>
    public IReadOnlyList<PortCheckRequest> BuildRequests(InstanceMetadata metadata, string? serverDir)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var configuration = LoadConfigurationSnapshot(metadata, serverDir);
        return BuildTargets(metadata, configuration, serverDir)
            .Select(target => CreateRequest(metadata, serverDir, target))
            .ToArray();
    }

    private IReadOnlyList<OccupiedTarget> GetOtherInstanceTargets(Guid currentInstanceId)
    {
        var results = new List<OccupiedTarget>();

        foreach (var other in _registry.GetAll())
        {
            if (other.Id == currentInstanceId)
            {
                continue;
            }

            string? otherServerDir = _registry.GetPath(other.Id);
            var otherConfiguration = LoadConfigurationSnapshot(other, otherServerDir);

            foreach (var target in BuildTargets(other, otherConfiguration, otherServerDir))
            {
                results.Add(
                    new OccupiedTarget(
                        other,
                        otherServerDir,
                        target,
                        _serverProcessManager.IsRunning(other.Id)));
            }
        }

        return results;
    }

    private PortCheckRequest CreateRequest(InstanceMetadata metadata, string? serverDir, PreflightTarget target)
    {
        return new PortCheckRequest(
            target.Port,
            target.Protocol,
            target.IpMode,
            target.BindAddress,
            metadata.Id,
            metadata.Name,
            serverDir,
            checkTunnelAvailability: false,
            checkPublicReachability: false,
            bindingRole: target.BindingRole,
            engine: target.Engine,
            displayName: target.Name);
    }

    private IReadOnlyList<PreflightTarget> BuildTargets(InstanceMetadata metadata, ServerConfiguration configuration, string? serverDir)
    {
        var targets = new List<PreflightTarget>();
        string? rawBindAddress = NormalizeConfiguredAddress(configuration.ServerIp);
        string? bindAddress = NormalizeBindAddress(rawBindAddress);
        int mainPort = ParsePortOrDefault(configuration.ServerPort, GetDefaultMainPort(metadata));

        if (IsNativeBedrockServer(metadata))
        {
            AppendBedrockTargets(targets, metadata, configuration, mainPort);
            return targets;
        }

        targets.Add(
            new PreflightTarget(
                "Java server",
                mainPort,
                PortProtocol.Tcp,
                DetermineJavaIpMode(rawBindAddress),
                bindAddress,
                GetDefaultMainPort(metadata),
                PortBindingRole.JavaServer,
                PortEngine.Java));

        if (metadata.HasGeyser)
        {
            GeyserNetworkSettings geyserSettings = LoadGeyserNetworkSettings(serverDir);
            int geyserPort = metadata.GeyserBedrockPort ?? DefaultBedrockPort;
            string? geyserBindAddress = NormalizeBindAddress(geyserSettings.BedrockAddress);

            targets.Add(
                new PreflightTarget(
                    "Geyser Bedrock",
                    geyserPort,
                    PortProtocol.Udp,
                    DetermineGeyserIpMode(geyserSettings.BedrockAddress),
                    geyserBindAddress,
                    DefaultBedrockPort,
                    PortBindingRole.GeyserBedrock,
                    PortEngine.Geyser));
        }

        return targets;
    }

    private void AppendBedrockTargets(List<PreflightTarget> targets, InstanceMetadata metadata, ServerConfiguration configuration, int mainPort)
    {
        PortBindingRole role = IsPocketMineServer(metadata)
            ? PortBindingRole.PocketMineServer
            : PortBindingRole.BedrockServer;
        PortEngine engine = IsPocketMineServer(metadata)
            ? PortEngine.PocketMine
            : PortEngine.BedrockDedicated;
        string targetName = IsPocketMineServer(metadata)
            ? "PocketMine server"
            : "Bedrock server";

        int? ipv6Port = TryGetPort(configuration, "server-portv6");
        if (ipv6Port.HasValue)
        {
            if (ipv6Port.Value == mainPort)
            {
                targets.Add(
                    new PreflightTarget(
                        targetName,
                        mainPort,
                        PortProtocol.Udp,
                        PortIpMode.DualStack,
                        BindAddress: null,
                        DefaultBedrockPort,
                        role,
                        engine));
            }
            else
            {
                targets.Add(
                    new PreflightTarget(
                        $"{targetName} IPv4",
                        mainPort,
                        PortProtocol.Udp,
                        PortIpMode.IPv4,
                        BindAddress: null,
                        DefaultBedrockPort,
                        role,
                        engine));

                targets.Add(
                    new PreflightTarget(
                        $"{targetName} IPv6",
                        ipv6Port.Value,
                        PortProtocol.Udp,
                        PortIpMode.IPv6,
                        BindAddress: null,
                        DefaultBedrockPort,
                        role,
                        engine));
            }

            return;
        }

        string? rawBindAddress = NormalizeConfiguredAddress(configuration.ServerIp);
        string? bindAddress = NormalizeBindAddress(rawBindAddress);
        targets.Add(
            new PreflightTarget(
                targetName,
                mainPort,
                PortProtocol.Udp,
                DetermineBedrockIpMode(rawBindAddress),
                bindAddress,
                DefaultBedrockPort,
                role,
                engine));
    }

    private ServerConfiguration LoadConfigurationSnapshot(InstanceMetadata metadata, string? serverDir)
    {
        if (!string.IsNullOrWhiteSpace(serverDir) && Directory.Exists(serverDir))
        {
            try
            {
                return _configurationService.Load(CloneMetadata(metadata), serverDir);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falling back to default port configuration for instance {InstanceName}.", metadata.Name);
            }
        }

        return new ServerConfiguration
        {
            ServerPort = GetDefaultMainPort(metadata).ToString(),
            ServerIp = string.Empty
        };
    }

    private static InstanceMetadata CloneMetadata(InstanceMetadata metadata)
    {
        return new InstanceMetadata
        {
            Id = metadata.Id,
            Name = metadata.Name,
            Description = metadata.Description,
            ServerType = metadata.ServerType,
            MinecraftVersion = metadata.MinecraftVersion,
            LoaderVersion = metadata.LoaderVersion,
            Motd = metadata.Motd,
            MaxPlayers = metadata.MaxPlayers,
            CreatedAt = metadata.CreatedAt,
            MinRamMb = metadata.MinRamMb,
            MaxRamMb = metadata.MaxRamMb,
            BackupIntervalHours = metadata.BackupIntervalHours,
            MaxBackupsToKeep = metadata.MaxBackupsToKeep,
            LastBackupTime = metadata.LastBackupTime,
            EnableAutoRestart = metadata.EnableAutoRestart,
            MaxAutoRestarts = metadata.MaxAutoRestarts,
            AutoRestartDelaySeconds = metadata.AutoRestartDelaySeconds,
            CustomJavaPath = metadata.CustomJavaPath,
            AdvancedJvmArgs = metadata.AdvancedJvmArgs,
            HasGeyser = metadata.HasGeyser
        };
    }

    private static int? TryGetPort(ServerConfiguration configuration, string key)
    {
        if (!configuration.AllProperties.TryGetValue(key, out string? rawValue))
        {
            return null;
        }

        return int.TryParse(rawValue, out int port) ? port : null;
    }

    private static int ParsePortOrDefault(string? rawValue, int defaultPort)
    {
        return int.TryParse(rawValue, out int port) ? port : defaultPort;
    }

    private static bool IsPortInValidRange(int port) => port >= MinValidPort && port <= MaxValidPort;

    private static bool IsPrivilegedPort(int port) => port >= MinValidPort && port <= MaxPrivilegedPort;

    private static int GetDefaultMainPort(InstanceMetadata metadata)
    {
        return IsNativeBedrockServer(metadata)
            ? DefaultBedrockPort
            : DefaultJavaPort;
    }

    private static bool IsNativeBedrockServer(InstanceMetadata metadata)
    {
        return IsBedrockDedicatedServer(metadata) || IsPocketMineServer(metadata);
    }

    private static bool IsBedrockDedicatedServer(InstanceMetadata metadata)
    {
        return metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsPocketMineServer(InstanceMetadata metadata)
    {
        return metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static PortIpMode DetermineJavaIpMode(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return PortIpMode.DualStack;
        }

        if (IPAddress.TryParse(bindAddress, out IPAddress? parsed))
        {
            return parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? PortIpMode.IPv6
                : PortIpMode.IPv4;
        }

        return PortIpMode.IPv4;
    }

    private static PortIpMode DetermineBedrockIpMode(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return PortIpMode.IPv4;
        }

        if (IPAddress.TryParse(bindAddress, out IPAddress? parsed))
        {
            return parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? PortIpMode.IPv6
                : PortIpMode.IPv4;
        }

        return PortIpMode.IPv4;
    }

    private static PortIpMode DetermineGeyserIpMode(string? rawBindAddress)
    {
        if (string.IsNullOrWhiteSpace(rawBindAddress))
        {
            return PortIpMode.IPv4;
        }

        string trimmed = rawBindAddress.Trim();
        if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
        {
            return PortIpMode.DualStack;
        }

        if (IPAddress.TryParse(trimmed, out IPAddress? parsed))
        {
            if (parsed.Equals(IPAddress.IPv6Any))
            {
                return PortIpMode.IPv6;
            }

            return parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? PortIpMode.IPv6
                : PortIpMode.IPv4;
        }

        return PortIpMode.IPv4;
    }

    private static string? NormalizeBindAddress(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return null;
        }

        string trimmed = bindAddress.Trim();
        if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (IPAddress.TryParse(trimmed, out IPAddress? parsed) &&
            (parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any)))
        {
            return null;
        }

        return trimmed;
    }

    private static string? NormalizeConfiguredAddress(string? bindAddress)
    {
        return string.IsNullOrWhiteSpace(bindAddress)
            ? null
            : bindAddress.Trim();
    }

    private static GeyserNetworkSettings LoadGeyserNetworkSettings(string? serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
        {
            return GeyserNetworkSettings.Default;
        }

        foreach (string configPath in EnumerateGeyserConfigPaths(serverDir))
        {
            if (!File.Exists(configPath))
            {
                continue;
            }

            if (TryReadGeyserNetworkSettings(configPath, out GeyserNetworkSettings settings))
            {
                return settings;
            }
        }

        return GeyserNetworkSettings.Default;
    }

    private static IEnumerable<string> EnumerateGeyserConfigPaths(string serverDir)
    {
        yield return Path.Combine(serverDir, "plugins", "Geyser-Spigot", "config.yml");
        yield return Path.Combine(serverDir, "plugins", "Geyser-Bukkit", "config.yml");
        yield return Path.Combine(serverDir, "plugins", "Geyser-Velocity", "config.yml");
        yield return Path.Combine(serverDir, "plugins", "Geyser-BungeeCord", "config.yml");
        yield return Path.Combine(serverDir, "config", "Geyser-Fabric", "config.yml");
        yield return Path.Combine(serverDir, "config", "Geyser-Forge", "config.yml");
        yield return Path.Combine(serverDir, "config", "Geyser-NeoForge", "config.yml");
        yield return Path.Combine(serverDir, "mods", "Geyser-Fabric", "config.yml");
        yield return Path.Combine(serverDir, "mods", "Geyser-Forge", "config.yml");
        yield return Path.Combine(serverDir, "mods", "Geyser-NeoForge", "config.yml");
    }

    private static bool TryReadGeyserNetworkSettings(string configPath, out GeyserNetworkSettings settings)
    {
        settings = GeyserNetworkSettings.Default;

        try
        {
            bool inBedrockSection = false;
            int bedrockIndent = -1;
            int? port = null;
            string? address = null;
            bool cloneRemotePort = false;

            foreach (string rawLine in File.ReadLines(configPath))
            {
                string line = StripYamlComment(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int indent = line.Length - line.TrimStart().Length;
                string trimmed = line.Trim();

                if (trimmed.Equals("bedrock:", StringComparison.OrdinalIgnoreCase))
                {
                    inBedrockSection = true;
                    bedrockIndent = indent;
                    continue;
                }

                if (!inBedrockSection)
                {
                    continue;
                }

                if (indent <= bedrockIndent)
                {
                    break;
                }

                int separatorIndex = trimmed.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = trimmed[..separatorIndex].Trim();
                string value = TrimYamlScalar(trimmed[(separatorIndex + 1)..]);

                if (key.Equals("port", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, out int parsedPort))
                {
                    port = parsedPort;
                }
                else if (key.Equals("address", StringComparison.OrdinalIgnoreCase))
                {
                    address = value;
                }
                else if (key.Equals("clone-remote-port", StringComparison.OrdinalIgnoreCase) &&
                         bool.TryParse(value, out bool parsedCloneRemotePort))
                {
                    cloneRemotePort = parsedCloneRemotePort;
                }
            }

            settings = new GeyserNetworkSettings(port, address, cloneRemotePort);
            return true;
        }
        catch
        {
            settings = GeyserNetworkSettings.Default;
            return false;
        }
    }

    private static string StripYamlComment(string line)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (current == '#' && !inSingleQuote && !inDoubleQuote)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string TrimYamlScalar(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static bool TargetsConflict(PreflightTarget current, PreflightTarget other)
    {
        return current.Port == other.Port &&
               ProtocolsOverlap(current.Protocol, other.Protocol) &&
               IpModesOverlap(current.IpMode, other.IpMode) &&
               BindAddressesOverlap(current.BindAddress, other.BindAddress);
    }

    private static bool ProtocolsOverlap(PortProtocol left, PortProtocol right)
    {
        if (left == PortProtocol.TcpAndUdp || right == PortProtocol.TcpAndUdp)
        {
            return true;
        }

        return left == right;
    }

    private static bool IpModesOverlap(PortIpMode left, PortIpMode right)
    {
        return left == PortIpMode.DualStack ||
               right == PortIpMode.DualStack ||
               left == right;
    }

    private static bool BindAddressesOverlap(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(left, out IPAddress? leftIp) &&
               IPAddress.TryParse(right, out IPAddress? rightIp) &&
               leftIp.Equals(rightIp);
    }

    private int FindSuggestedPort(PreflightTarget target, IReadOnlyList<OccupiedTarget> occupiedTargets)
    {
        var occupiedPorts = occupiedTargets
            .Where(x =>
                ProtocolsOverlap(target.Protocol, x.Target.Protocol) &&
                IpModesOverlap(target.IpMode, x.Target.IpMode) &&
                BindAddressesOverlap(target.BindAddress, x.Target.BindAddress))
            .Select(x => x.Target.Port)
            .ToHashSet();

        int candidate = Math.Max(target.DefaultPort, IsPrivilegedPort(target.Port) ? target.DefaultPort : target.Port + 1);
        while (candidate <= MaxValidPort)
        {
            if (!occupiedPorts.Contains(candidate) && !IsPrivilegedPort(candidate))
            {
                return candidate;
            }

            candidate++;
        }

        return target.DefaultPort;
    }

    private static string BuildConflictMessage(PreflightTarget target, PortConflictInfo conflict)
    {
        string instanceName = conflict.ExistingLease?.InstanceName ?? "another PocketMC instance";
        return $"{target.Name} port {target.Port} ({FormatProtocol(target.Protocol)}) conflicts with '{instanceName}'.";
    }

    private static string FormatProtocol(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            PortProtocol.TcpAndUdp => "TCP/UDP",
            _ => protocol.ToString()
        };
    }

    private sealed record PreflightTarget(
        string Name,
        int Port,
        PortProtocol Protocol,
        PortIpMode IpMode,
        string? BindAddress,
        int DefaultPort,
        PortBindingRole BindingRole,
        PortEngine Engine);

    private sealed record OccupiedTarget(
        InstanceMetadata Metadata,
        string? ServerDir,
        PreflightTarget Target,
        bool IsRunning);

    private sealed record DetectedConflict(
        PreflightTarget Target,
        PortConflictInfo Conflict);

    private sealed record GeyserNetworkSettings(
        int? BedrockPort,
        string? BedrockAddress,
        bool CloneRemotePort)
    {
        public static GeyserNetworkSettings Default { get; } = new(PortPreflightService.DefaultBedrockPort, "0.0.0.0", false);
    }
}
