using System;
using System.Collections.Generic;
using System.Linq;

namespace PocketMC.Desktop.Features.Networking;

/// <summary>
/// Converts structured port reliability failures into concise user-facing copy.
/// </summary>
public sealed class PortFailureMessageService
{
    /// <summary>
    /// Builds display copy for a port reliability exception.
    /// </summary>
    /// <param name="exception">The structured port reliability exception.</param>
    /// <param name="instanceName">The instance name to include in user copy.</param>
    /// <returns>User-facing display copy for dialogs and badges.</returns>
    public PortFailureDisplayInfo CreateDisplayInfo(PortReliabilityException exception, string instanceName)
    {
        ArgumentNullException.ThrowIfNull(exception);

        PortCheckResult primary = exception.PrimaryResult;
        string title = GetTitle(primary);
        string explanation = GetExplanation(primary, instanceName);
        string action = GetAction(primary);

        string message = string.IsNullOrWhiteSpace(action)
            ? explanation
            : $"{explanation}\n\nWhat to do: {action}";

        return new PortFailureDisplayInfo(
            title,
            message,
            GetBadgeText(primary),
            action);
    }

    private static string GetTitle(PortCheckResult result)
    {
        return result.FailureCode switch
        {
            PortFailureCode.InvalidRange => "Invalid Port",
            PortFailureCode.ReservedOrPrivilegedPort => "Port Needs Attention",
            PortFailureCode.InUseByPocketMcInstance => "Port Used By Another Instance",
            PortFailureCode.InUseByExternalProcess or PortFailureCode.TcpConflict or PortFailureCode.UdpConflict => "Port Already In Use",
            PortFailureCode.AccessDenied => "Port Access Denied",
            PortFailureCode.IPv4BindFailure or PortFailureCode.IPv6BindFailure => "Port Bind Failed",
            PortFailureCode.UnsupportedProtocolOrAddressFamily => "Network Mode Not Supported",
            PortFailureCode.TunnelLimitReached => "Tunnel Limit Reached",
            PortFailureCode.PlayitAgentOffline => "Playit Agent Offline",
            PortFailureCode.PlayitTokenInvalid => "Playit Reconnect Required",
            PortFailureCode.PlayitClaimRequired => "Connect Playit",
            PortFailureCode.PublicReachabilityFailure => "Public Connection Failed",
            _ => "Port Check Failed"
        };
    }

    private static string GetExplanation(PortCheckResult result, string instanceName)
    {
        string portText = FormatEndpoint(result.Request);
        return result.FailureCode switch
        {
            PortFailureCode.InvalidRange =>
                $"PocketMC could not start '{instanceName}' because {portText} is outside the valid range 1-65535.",

            PortFailureCode.ReservedOrPrivilegedPort =>
                $"'{instanceName}' is configured to use {portText}, which is a low-numbered system port.",

            PortFailureCode.InUseByPocketMcInstance =>
                $"PocketMC could not start '{instanceName}' because {portText} is already assigned to another PocketMC instance.",

            PortFailureCode.InUseByExternalProcess or PortFailureCode.TcpConflict or PortFailureCode.UdpConflict =>
                $"PocketMC could not start '{instanceName}' because {portText} is already being used.",

            PortFailureCode.AccessDenied =>
                $"Windows denied access to {portText}.",

            PortFailureCode.IPv4BindFailure =>
                $"PocketMC could not bind IPv4 for '{instanceName}' on {portText}.",

            PortFailureCode.IPv6BindFailure =>
                $"PocketMC could not bind IPv6 for '{instanceName}' on {portText}.",

            PortFailureCode.UnsupportedProtocolOrAddressFamily =>
                $"The configured network mode for '{instanceName}' is not supported on this machine or bind address.",

            PortFailureCode.TunnelLimitReached =>
                "PocketMC could not create or match a Playit tunnel because your account has reached its tunnel limit.",

            PortFailureCode.PlayitAgentOffline =>
                "PocketMC could not complete public tunnel setup because the Playit agent is not connected.",

            PortFailureCode.PlayitTokenInvalid =>
                "PocketMC could not reach Playit because the saved agent token is invalid or expired.",

            PortFailureCode.PlayitClaimRequired =>
                "PocketMC needs a linked Playit agent before it can use Playit tunnels.",

            PortFailureCode.PublicReachabilityFailure =>
                $"'{instanceName}' may be running locally, but PocketMC could not resolve the expected public address for {portText}.",

            _ =>
                $"PocketMC could not validate the port setup for '{instanceName}'."
        };
    }

    private static string GetAction(PortCheckResult result)
    {
        PortRecoveryRecommendation? recommendation = result.Recommendations.FirstOrDefault();
        string? suggestedPortText = BuildSuggestedPortText(result.Recommendations);

        return result.FailureCode switch
        {
            PortFailureCode.InvalidRange =>
                suggestedPortText ?? "Open Settings and choose a port between 1 and 65535.",

            PortFailureCode.ReservedOrPrivilegedPort =>
                suggestedPortText ?? "Use a higher port such as 25565 for Java or 19132 for Bedrock.",

            PortFailureCode.InUseByPocketMcInstance =>
                BuildPocketMcConflictAction(result, suggestedPortText),

            PortFailureCode.InUseByExternalProcess or PortFailureCode.TcpConflict or PortFailureCode.UdpConflict =>
                BuildExternalConflictAction(result, suggestedPortText),

            PortFailureCode.AccessDenied =>
                "Choose a higher non-system port, or check whether security software is blocking PocketMC.",

            PortFailureCode.IPv4BindFailure or PortFailureCode.IPv6BindFailure =>
                BuildBindFailureAction(result, suggestedPortText),

            PortFailureCode.UnsupportedProtocolOrAddressFamily =>
                "Clear the custom server IP or switch to an address that matches the selected IP mode.",

            PortFailureCode.TunnelLimitReached =>
                "Delete an unused tunnel in Playit, then try again.",

            PortFailureCode.PlayitAgentOffline =>
                "Open the Tunnel page and start or reconnect the Playit agent.",

            PortFailureCode.PlayitTokenInvalid =>
                "Open the Tunnel page and reconnect Playit.",

            PortFailureCode.PlayitClaimRequired =>
                "Open the Tunnel page, create a Playit setup code, and link PocketMC before starting the server again.",

            PortFailureCode.PublicReachabilityFailure =>
                BuildPublicReachabilityAction(result),

            _ when recommendation != null && !string.IsNullOrWhiteSpace(recommendation.Description) =>
                recommendation.Description,

            _ =>
                "Check the server port in Settings and try again."
        };
    }

    private static string BuildPocketMcConflictAction(PortCheckResult result, string? suggestedPortText)
    {
        PortConflictInfo? conflict = result.Conflicts.FirstOrDefault(x => x.ExistingLease != null);
        string owner = conflict?.ExistingLease?.InstanceName ?? "the other instance";
        return suggestedPortText == null
            ? $"Stop {owner}, or choose another port in Settings."
            : $"Stop {owner}, or {suggestedPortText}";
    }

    private static string BuildExternalConflictAction(PortCheckResult result, string? suggestedPortText)
    {
        string baseAction = suggestedPortText ?? "choose another port in Settings.";
        if (result.Request.Protocol == PortProtocol.Udp)
        {
            return $"Close the app using this UDP port, {baseAction} Also check Windows Firewall and Bedrock loopback settings for Bedrock/Geyser servers.";
        }

        return $"Close the app using this port, or {baseAction}";
    }

    private static string BuildBindFailureAction(PortCheckResult result, string? suggestedPortText)
    {
        string baseAction = string.IsNullOrWhiteSpace(result.Request.BindAddress)
            ? "Check Windows Firewall and local network settings."
            : "Clear the custom server IP in Settings, or use an IP address assigned to this PC.";

        if (result.Request.Protocol == PortProtocol.Udp || result.Request.Port == 19132)
        {
            baseAction += " For Bedrock or Geyser, also check Bedrock loopback restrictions.";
        }

        return suggestedPortText == null ? baseAction : $"{baseAction} You can also {suggestedPortText}";
    }

    private static string BuildPublicReachabilityAction(PortCheckResult result)
    {
        if (result.Request.Engine == PortEngine.Geyser || result.Request.BindingRole == PortBindingRole.GeyserBedrock)
        {
            return "Confirm the Playit Bedrock tunnel targets the Geyser UDP port, then check Windows Firewall and Bedrock loopback settings.";
        }

        if (result.Request.Protocol == PortProtocol.Udp ||
            result.Request.Engine is PortEngine.BedrockDedicated or PortEngine.PocketMine)
        {
            return "Confirm the Playit tunnel is a Minecraft Bedrock tunnel for this UDP port, then check Windows Firewall and Bedrock loopback settings.";
        }

        return "Confirm the Playit tunnel targets this TCP port, then check Windows Firewall or router forwarding.";
    }

    private static string? BuildSuggestedPortText(IEnumerable<PortRecoveryRecommendation> recommendations)
    {
        PortRecoveryRecommendation? recommendation = recommendations.FirstOrDefault(x => x.SuggestedPort.HasValue);
        if (recommendation?.SuggestedPort == null)
        {
            return null;
        }

        return $"choose port {recommendation.SuggestedPort.Value} in Settings.";
    }

    private static string GetBadgeText(PortCheckResult result)
    {
        return result.FailureCode switch
        {
            PortFailureCode.InUseByPocketMcInstance => "Port: instance",
            PortFailureCode.InUseByExternalProcess or PortFailureCode.TcpConflict or PortFailureCode.UdpConflict => "Port: in use",
            PortFailureCode.PlayitAgentOffline => "Port: Playit",
            PortFailureCode.PlayitTokenInvalid => "Port: reconnect",
            PortFailureCode.PlayitClaimRequired => "Port: connect",
            PortFailureCode.PublicReachabilityFailure => "Port: public",
            _ => "Port issue"
        };
    }

    private static string FormatEndpoint(PortCheckRequest request)
    {
        string protocol = request.Protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            PortProtocol.TcpAndUdp => "TCP/UDP",
            _ => request.Protocol.ToString()
        };

        string ipMode = request.IpMode switch
        {
            PortIpMode.IPv4 => "IPv4",
            PortIpMode.IPv6 => "IPv6",
            PortIpMode.DualStack => "IPv4/IPv6",
            _ => request.IpMode.ToString()
        };

        string bindAddress = string.IsNullOrWhiteSpace(request.BindAddress)
            ? string.Empty
            : $" on {request.BindAddress}";

        string purpose = string.IsNullOrWhiteSpace(request.DisplayName)
            ? "server"
            : request.DisplayName;

        return $"{purpose} {protocol} {ipMode} port {request.Port}{bindAddress}";
    }
}
