namespace PocketMC.Domain.Models;

/// <summary>
/// Identifies the specific reason a port validation or bind check failed.
/// </summary>
public enum PortFailureCode
{
    /// <summary>
    /// No port failure was detected.
    /// </summary>
    None = 0,

    /// <summary>
    /// The configured port is outside the valid 1-65535 range.
    /// </summary>
    InvalidRange,

    /// <summary>
    /// The configured port falls into a reserved or privileged range that PocketMC should avoid.
    /// </summary>
    ReservedOrPrivilegedPort,

    /// <summary>
    /// The port is already assigned to another PocketMC instance.
    /// </summary>
    InUseByPocketMcInstance,

    /// <summary>
    /// The port is already owned by a process outside PocketMC.
    /// </summary>
    InUseByExternalProcess,

    /// <summary>
    /// The operating system denied access when PocketMC tried to inspect or bind the port.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// An IPv4 bind attempt failed.
    /// </summary>
    IPv4BindFailure,

    /// <summary>
    /// An IPv6 bind attempt failed.
    /// </summary>
    IPv6BindFailure,

    /// <summary>
    /// A TCP listener conflict was detected.
    /// </summary>
    TcpConflict,

    /// <summary>
    /// A UDP listener conflict was detected.
    /// </summary>
    UdpConflict,

    /// <summary>
    /// The requested protocol or address family is not supported on the current system or by the configured bind address.
    /// </summary>
    UnsupportedProtocolOrAddressFamily,

    /// <summary>
    /// No additional public tunnels can be created for the requested port.
    /// </summary>
    TunnelLimitReached,

    /// <summary>
    /// The Playit agent is not online, claimed, or ready to resolve tunnels.
    /// </summary>
    PlayitAgentOffline,

    /// <summary>
    /// The Playit token or agent secret is invalid, revoked, or expired.
    /// </summary>
    PlayitTokenInvalid,

    /// <summary>
    /// The Playit agent still needs account claim approval before tunnel work can continue.
    /// </summary>
    PlayitClaimRequired,

    /// <summary>
    /// The port bound locally, but PocketMC could not verify expected public reachability.
    /// </summary>
    PublicReachabilityFailure,

    /// <summary>
    /// A socket-level failure occurred, but it could not be classified more precisely.
    /// </summary>
    UnknownSocketFailure,

    /// <summary>
    /// A port-related failure occurred, but it could not be classified more precisely.
    /// </summary>
    UnknownPortFailure
}
