using PocketMC.Domain.Models;
using PocketMC.Application.Services.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Networking;

/// <summary>
/// Performs OS-level socket probing to determine whether ports can be bound locally.
/// </summary>
public sealed class PortProbeService
{
    private readonly ILogger<PortProbeService> _logger;

    /// <summary>
    /// Initializes a new port probe service.
    /// </summary>
    public PortProbeService(ILogger<PortProbeService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Probes a single port request against the local operating system.
    /// </summary>
    /// <param name="request">The port request to probe.</param>
    /// <returns>A structured port probe result.</returns>
    public PortCheckResult Probe(PortCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Port < 1 || request.Port > 65535)
        {
            return new PortCheckResult(
                request,
                isSuccessful: false,
                canBindLocally: false,
                failureCode: PortFailureCode.InvalidRange,
                failureMessage: $"Port {request.Port} is outside the valid range 1-65535.");
        }

        var conflicts = new List<PortConflictInfo>();
        var failures = new List<ProbeFailure>();

        foreach (PortProtocol protocol in ExpandProtocols(request.Protocol))
        {
            foreach (AddressFamily addressFamily in ExpandAddressFamilies(request.IpMode))
            {
                if (!TryProbeSocket(request, protocol, addressFamily, out ProbeFailure? failure, out PortConflictInfo? conflict))
                {
                    if (failure != null)
                    {
                        failures.Add(failure);
                    }

                    if (conflict != null)
                    {
                        conflicts.Add(conflict);
                    }
                }
            }
        }

        if (failures.Count == 0)
        {
            return new PortCheckResult(
                request,
                isSuccessful: true,
                canBindLocally: true,
                failureCode: PortFailureCode.None,
                failureMessage: null,
                lease: null,
                conflicts: Array.Empty<PortConflictInfo>());
        }

        ProbeFailure primaryFailure = SelectPrimaryFailure(failures);

        return new PortCheckResult(
            request,
            isSuccessful: false,
            canBindLocally: false,
            failureCode: primaryFailure.FailureCode,
            failureMessage: primaryFailure.Message,
            lease: null,
            conflicts: conflicts);
    }

    /// <summary>
    /// Probes a set of requests and returns the results in input order.
    /// </summary>
    /// <param name="requests">The port requests to probe.</param>
    /// <returns>The probe results for the supplied requests.</returns>
    public IReadOnlyList<PortCheckResult> ProbeMany(IEnumerable<PortCheckRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return requests.Select(Probe).ToArray();
    }

    private bool TryProbeSocket(
        PortCheckRequest request,
        PortProtocol protocol,
        AddressFamily addressFamily,
        out ProbeFailure? failure,
        out PortConflictInfo? conflict)
    {
        failure = null;
        conflict = null;

        if (!TryCreateEndpoint(request, addressFamily, out IPEndPoint? endpoint, out ProbeFailure? endpointFailure))
        {
            failure = endpointFailure;
            return false;
        }

        try
        {
            using Socket socket = CreateSocket(protocol, addressFamily);
            ConfigureSocket(socket, protocol);
            socket.Bind(endpoint!);

            if (protocol == PortProtocol.Tcp)
            {
                socket.Listen(backlog: 1);
            }

            return true;
        }
        catch (SocketException ex)
        {
            PortFailureCode failureCode = PortProbeFailureClassifier.Classify(ex, protocol, addressFamily);
            failure = new ProbeFailure(
                failureCode,
                BuildFailureMessage(request, protocol, addressFamily, failureCode, ex.Message));

            conflict = TryCreateConflictInfo(request, protocol, addressFamily, failureCode, ex.Message);
            _logger.LogDebug(
                ex,
                "Port probe failed for port {Port} ({Protocol}, {Family}). Classified as {FailureCode}.",
                request.Port,
                protocol,
                addressFamily,
                failureCode);
            return false;
        }
        catch (Exception ex)
        {
            PortFailureCode failureCode = PortProbeFailureClassifier.Classify(ex, addressFamily);
            failure = new ProbeFailure(
                failureCode,
                BuildFailureMessage(request, protocol, addressFamily, failureCode, ex.Message));

            _logger.LogDebug(
                ex,
                "Port probe raised a non-socket failure for port {Port} ({Protocol}, {Family}). Classified as {FailureCode}.",
                request.Port,
                protocol,
                addressFamily,
                failureCode);
            return false;
        }
    }

    private static Socket CreateSocket(PortProtocol protocol, AddressFamily addressFamily)
    {
        return protocol switch
        {
            PortProtocol.Udp => new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp),
            _ => new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp)
        };
    }

    private static void ConfigureSocket(Socket socket, PortProtocol protocol)
    {
        socket.ExclusiveAddressUse = true;

        if (socket.AddressFamily == AddressFamily.InterNetworkV6)
        {
            try
            {
                socket.DualMode = false;
            }
            catch (SocketException)
            {
                // Best effort only. Unsupported stacks will be classified during bind.
            }
            catch (NotSupportedException)
            {
                // Best effort only. Unsupported stacks will be classified during bind.
            }
        }

        if (protocol == PortProtocol.Tcp)
        {
            socket.NoDelay = true;
        }
    }

    private static bool TryCreateEndpoint(
        PortCheckRequest request,
        AddressFamily addressFamily,
        out IPEndPoint? endpoint,
        out ProbeFailure? failure)
    {
        endpoint = null;
        failure = null;

        if (string.IsNullOrWhiteSpace(request.BindAddress))
        {
            endpoint = addressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, request.Port)
                : new IPEndPoint(IPAddress.Any, request.Port);
            return true;
        }

        if (!IPAddress.TryParse(request.BindAddress, out IPAddress? bindAddress))
        {
            failure = new ProbeFailure(
                addressFamily == AddressFamily.InterNetworkV6
                    ? PortFailureCode.IPv6BindFailure
                    : PortFailureCode.IPv4BindFailure,
                $"Bind address '{request.BindAddress}' is not a valid IP address for probing.");
            return false;
        }

        if (bindAddress.AddressFamily != addressFamily)
        {
            failure = new ProbeFailure(
                PortFailureCode.UnsupportedProtocolOrAddressFamily,
                $"Bind address '{request.BindAddress}' does not match the requested {FormatAddressFamily(addressFamily)} probe.");
            return false;
        }

        endpoint = new IPEndPoint(bindAddress, request.Port);
        return true;
    }

    private static IEnumerable<PortProtocol> ExpandProtocols(PortProtocol protocol)
    {
        return protocol == PortProtocol.TcpAndUdp
            ? new[] { PortProtocol.Tcp, PortProtocol.Udp }
            : new[] { protocol };
    }

    private static IEnumerable<AddressFamily> ExpandAddressFamilies(PortIpMode ipMode)
    {
        return ipMode switch
        {
            PortIpMode.IPv6 => new[] { AddressFamily.InterNetworkV6 },
            PortIpMode.DualStack => new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 },
            _ => new[] { AddressFamily.InterNetwork }
        };
    }

    private static ProbeFailure SelectPrimaryFailure(IReadOnlyList<ProbeFailure> failures)
    {
        return failures
            .OrderBy(GetFailurePriority)
            .First();
    }

    private static int GetFailurePriority(ProbeFailure failure)
    {
        return failure.FailureCode switch
        {
            PortFailureCode.AccessDenied => 0,
            PortFailureCode.TcpConflict => 1,
            PortFailureCode.UdpConflict => 1,
            PortFailureCode.UnsupportedProtocolOrAddressFamily => 2,
            PortFailureCode.IPv4BindFailure => 3,
            PortFailureCode.IPv6BindFailure => 3,
            PortFailureCode.UnknownSocketFailure => 4,
            _ => 5
        };
    }

    private static string BuildFailureMessage(
        PortCheckRequest request,
        PortProtocol protocol,
        AddressFamily addressFamily,
        PortFailureCode failureCode,
        string? rawMessage)
    {
        string protocolText = protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            _ => "TCP/UDP"
        };

        string familyText = FormatAddressFamily(addressFamily);
        string bindingText = string.IsNullOrWhiteSpace(request.DisplayName) ? "port" : $"{request.DisplayName} port";
        string suffix = string.IsNullOrWhiteSpace(rawMessage) ? string.Empty : $" {rawMessage}".TrimEnd();

        return failureCode switch
        {
            PortFailureCode.AccessDenied => $"Access was denied while probing {protocolText} {familyText} on {bindingText} {request.Port}.{suffix}",
            PortFailureCode.TcpConflict or PortFailureCode.UdpConflict => $"{bindingText} {request.Port} is already in use for {protocolText} {familyText}.{suffix}",
            PortFailureCode.UnsupportedProtocolOrAddressFamily => $"{protocolText} {familyText} probing is not supported for {bindingText} {request.Port}.{suffix}",
            PortFailureCode.IPv4BindFailure or PortFailureCode.IPv6BindFailure => $"PocketMC could not bind {protocolText} {familyText} on {bindingText} {request.Port}.{suffix}",
            PortFailureCode.UnknownSocketFailure => $"An unexpected socket failure occurred while probing {bindingText} {request.Port} for {protocolText} {familyText}.{suffix}",
            _ => $"Port probe failed for {bindingText} {request.Port}.{suffix}"
        };
    }

    private static PortConflictInfo? TryCreateConflictInfo(
        PortCheckRequest request,
        PortProtocol protocol,
        AddressFamily addressFamily,
        PortFailureCode failureCode,
        string? rawMessage)
    {
        if (failureCode is not (PortFailureCode.TcpConflict or PortFailureCode.UdpConflict))
        {
            return null;
        }

        PortConflictInfo? listenerConflict = TryFindListenerConflict(request.Port, protocol, addressFamily, request.BindAddress);
        if (listenerConflict != null)
        {
            return listenerConflict;
        }

        return new PortConflictInfo(
            PortFailureCode.InUseByExternalProcess,
            request.Port,
            protocol,
            addressFamily == AddressFamily.InterNetworkV6 ? PortIpMode.IPv6 : PortIpMode.IPv4,
            request.BindAddress,
            existingLease: null,
            processId: null,
            processName: null,
            details: string.IsNullOrWhiteSpace(rawMessage)
                ? "Socket bind reported that the port is already in use."
                : $"Socket bind reported that the port is already in use: {rawMessage}");
    }

    private static PortConflictInfo? TryFindListenerConflict(int port, PortProtocol protocol, AddressFamily addressFamily, string? bindAddress)
    {
        try
        {
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            IEnumerable<IPEndPoint> listeners = protocol == PortProtocol.Udp
                ? properties.GetActiveUdpListeners()
                : properties.GetActiveTcpListeners();

            IPEndPoint? match = listeners.FirstOrDefault(listener =>
                listener.Port == port &&
                listener.Address.AddressFamily == addressFamily &&
                ListenerAddressesOverlap(listener.Address, bindAddress));

            if (match == null)
            {
                return null;
            }

            return new PortConflictInfo(
                PortFailureCode.InUseByExternalProcess,
                port,
                protocol,
                addressFamily == AddressFamily.InterNetworkV6 ? PortIpMode.IPv6 : PortIpMode.IPv4,
                bindAddress,
                existingLease: null,
                processId: null,
                processName: null,
                details: $"An active {protocol} listener is already present at {match.Address}:{match.Port}.");
        }
        catch
        {
            return null;
        }
    }

    private static bool ListenerAddressesOverlap(IPAddress listenerAddress, string? requestedBindAddress)
    {
        if (string.IsNullOrWhiteSpace(requestedBindAddress))
        {
            return true;
        }

        if (!IPAddress.TryParse(requestedBindAddress, out IPAddress? requestedAddress))
        {
            return true;
        }

        if (listenerAddress.Equals(requestedAddress))
        {
            return true;
        }

        return listenerAddress.Equals(IPAddress.Any) ||
               listenerAddress.Equals(IPAddress.IPv6Any) ||
               requestedAddress.Equals(IPAddress.Any) ||
               requestedAddress.Equals(IPAddress.IPv6Any);
    }

    private static string FormatAddressFamily(AddressFamily addressFamily)
    {
        return addressFamily == AddressFamily.InterNetworkV6
            ? "IPv6"
            : "IPv4";
    }

    private sealed record ProbeFailure(
        PortFailureCode FailureCode,
        string Message);
}
