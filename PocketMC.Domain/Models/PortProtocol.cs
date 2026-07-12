namespace PocketMC.Domain.Models;

/// <summary>
/// Represents the transport protocol a server port needs to bind.
/// </summary>
public enum PortProtocol
{
    /// <summary>
    /// The port is bound over TCP only.
    /// </summary>
    Tcp,

    /// <summary>
    /// The port is bound over UDP only.
    /// </summary>
    Udp,

    /// <summary>
    /// The port must be available for both TCP and UDP binding.
    /// </summary>
    TcpAndUdp
}
