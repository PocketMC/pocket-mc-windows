using System;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Describes a single port validation request for a PocketMC instance.
/// </summary>
public sealed class PortCheckRequest
{
    /// <summary>
    /// Initializes a new port check request.
    /// </summary>
    /// <param name="port">The local port to validate.</param>
    /// <param name="protocol">The protocol that must be available.</param>
    /// <param name="ipMode">The IP stack mode that should be validated.</param>
    /// <param name="bindAddress">The configured bind address, if any.</param>
    /// <param name="instanceId">The owning PocketMC instance identifier, if known.</param>
    /// <param name="instanceName">The owning PocketMC instance name, if known.</param>
    /// <param name="instancePath">The owning PocketMC instance path, if known.</param>
    /// <param name="checkTunnelAvailability">Whether Playit tunnel readiness should be included.</param>
    /// <param name="checkPublicReachability">Whether public reachability should be included.</param>
    /// <param name="bindingRole">The server-facing purpose of the port binding.</param>
    /// <param name="engine">The server engine responsible for this binding.</param>
    /// <param name="displayName">A concise human-readable name for diagnostics and UI.</param>
    public PortCheckRequest(
        int port,
        PortProtocol protocol = PortProtocol.Tcp,
        PortIpMode ipMode = PortIpMode.IPv4,
        string? bindAddress = null,
        Guid? instanceId = null,
        string? instanceName = null,
        string? instancePath = null,
        bool checkTunnelAvailability = false,
        bool checkPublicReachability = false,
        PortBindingRole bindingRole = PortBindingRole.PrimaryServer,
        PortEngine engine = PortEngine.Unknown,
        string? displayName = null)
    {
        Port = port;
        Protocol = protocol;
        IpMode = ipMode;
        BindAddress = bindAddress;
        InstanceId = instanceId;
        InstanceName = instanceName;
        InstancePath = instancePath;
        CheckTunnelAvailability = checkTunnelAvailability;
        CheckPublicReachability = checkPublicReachability;
        BindingRole = bindingRole;
        Engine = engine;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? BuildDefaultDisplayName(bindingRole, engine) : displayName.Trim();
    }

    /// <summary>
    /// Gets the local port to validate.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the required transport protocol.
    /// </summary>
    public PortProtocol Protocol { get; }

    /// <summary>
    /// Gets the requested IP validation mode.
    /// </summary>
    public PortIpMode IpMode { get; }

    /// <summary>
    /// Gets the configured bind address. Empty or <see langword="null"/> means the server default.
    /// </summary>
    public string? BindAddress { get; }

    /// <summary>
    /// Gets the owning PocketMC instance identifier when the request is tied to a known instance.
    /// </summary>
    public Guid? InstanceId { get; }

    /// <summary>
    /// Gets the owning PocketMC instance name when the request is tied to a known instance.
    /// </summary>
    public string? InstanceName { get; }

    /// <summary>
    /// Gets the owning PocketMC instance path when the request is tied to a known instance.
    /// </summary>
    public string? InstancePath { get; }

    /// <summary>
    /// Gets a value indicating whether tunnel readiness should be included in the validation.
    /// </summary>
    public bool CheckTunnelAvailability { get; }

    /// <summary>
    /// Gets a value indicating whether public reachability should be included in the validation.
    /// </summary>
    public bool CheckPublicReachability { get; }

    /// <summary>
    /// Gets the server-facing purpose of this port binding.
    /// </summary>
    public PortBindingRole BindingRole { get; }

    /// <summary>
    /// Gets the server engine responsible for this binding.
    /// </summary>
    public PortEngine Engine { get; }

    /// <summary>
    /// Gets a concise human-readable binding name for diagnostics and UI.
    /// </summary>
    public string DisplayName { get; }

    private static string BuildDefaultDisplayName(PortBindingRole bindingRole, PortEngine engine)
    {
        return bindingRole switch
        {
            PortBindingRole.JavaServer => "Java server",
            PortBindingRole.BedrockServer => "Bedrock server",
            PortBindingRole.PocketMineServer => "PocketMine server",
            PortBindingRole.GeyserBedrock => "Geyser Bedrock",
            PortBindingRole.SimpleVoiceChat => "Simple Voice Chat",
            _ => engine switch
            {
                PortEngine.Java => "Java server",
                PortEngine.BedrockDedicated => "Bedrock server",
                PortEngine.PocketMine => "PocketMine server",
                PortEngine.Geyser => "Geyser Bedrock",
                PortEngine.SimpleVoiceChat => "Simple Voice Chat",
                _ => "Server"
            }
        };
    }
}
