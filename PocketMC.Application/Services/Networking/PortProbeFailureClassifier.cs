using System;
using System.Net.Sockets;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Maps low-level socket failures to PocketMC port failure codes.
/// </summary>
public static class PortProbeFailureClassifier
{
    /// <summary>
    /// Classifies a socket exception into a PocketMC port failure code.
    /// </summary>
    /// <param name="exception">The socket exception to classify.</param>
    /// <param name="protocol">The protocol being probed.</param>
    /// <param name="addressFamily">The address family being probed.</param>
    /// <returns>The mapped <see cref="PortFailureCode"/>.</returns>
    public static PortFailureCode Classify(SocketException exception, PortProtocol protocol, AddressFamily addressFamily)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.SocketErrorCode switch
        {
            SocketError.AccessDenied => PortFailureCode.AccessDenied,
            SocketError.AddressAlreadyInUse => protocol == PortProtocol.Udp
                ? PortFailureCode.UdpConflict
                : PortFailureCode.TcpConflict,
            SocketError.AddressFamilyNotSupported => PortFailureCode.UnsupportedProtocolOrAddressFamily,
            SocketError.ProtocolFamilyNotSupported => PortFailureCode.UnsupportedProtocolOrAddressFamily,
            SocketError.ProtocolNotSupported => PortFailureCode.UnsupportedProtocolOrAddressFamily,
            SocketError.OperationNotSupported => PortFailureCode.UnsupportedProtocolOrAddressFamily,
            SocketError.AddressNotAvailable => MapBindFailure(addressFamily),
            SocketError.InvalidArgument => MapBindFailure(addressFamily),
            SocketError.Fault => MapBindFailure(addressFamily),
            _ => PortFailureCode.UnknownSocketFailure
        };
    }

    /// <summary>
    /// Classifies a non-socket exception raised during probing.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <param name="addressFamily">The address family being probed.</param>
    /// <returns>The mapped <see cref="PortFailureCode"/>.</returns>
    public static PortFailureCode Classify(Exception exception, AddressFamily addressFamily)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            PlatformNotSupportedException => PortFailureCode.UnsupportedProtocolOrAddressFamily,
            NotSupportedException => PortFailureCode.UnsupportedProtocolOrAddressFamily,
            ObjectDisposedException => PortFailureCode.UnknownSocketFailure,
            _ => MapBindFailure(addressFamily)
        };
    }

    private static PortFailureCode MapBindFailure(AddressFamily addressFamily)
    {
        return addressFamily == AddressFamily.InterNetworkV6
            ? PortFailureCode.IPv6BindFailure
            : PortFailureCode.IPv4BindFailure;
    }
}
