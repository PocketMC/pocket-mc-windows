namespace PocketMC.Domain.Models;

/// <summary>
/// Describes which IP stack a port should be validated against.
/// </summary>
public enum PortIpMode
{
    /// <summary>
    /// Validate or bind against IPv4 only.
    /// </summary>
    IPv4,

    /// <summary>
    /// Validate or bind against IPv6 only.
    /// </summary>
    IPv6,

    /// <summary>
    /// Validate or bind against both IPv4 and IPv6.
    /// </summary>
    DualStack
}
