using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Tests;

public sealed class PortProbeServiceTests
{
    [Fact]
    public void Probe_WhenTcpPortIsAlreadyBound_ReturnsTcpConflict()
    {
        var service = new PortProbeService(NullLogger<PortProbeService>.Instance);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        PortCheckResult result = service.Probe(new PortCheckRequest(
            port,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            bindAddress: IPAddress.Loopback.ToString(),
            displayName: "Java server"));

        Assert.False(result.IsSuccessful);
        Assert.Equal(PortFailureCode.TcpConflict, result.FailureCode);
        Assert.NotEmpty(result.Conflicts);
        Assert.Equal(PortProtocol.Tcp, result.Conflicts[0].Protocol);
    }

    [Fact]
    public void Probe_WhenUdpPortIsAlreadyBound_ReturnsUdpConflict()
    {
        var service = new PortProbeService(NullLogger<PortProbeService>.Instance);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ExclusiveAddressUse = true
        };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)socket.LocalEndPoint!).Port;

        PortCheckResult result = service.Probe(new PortCheckRequest(
            port,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindAddress: IPAddress.Loopback.ToString(),
            displayName: "Bedrock server"));

        Assert.False(result.IsSuccessful);
        Assert.Equal(PortFailureCode.UdpConflict, result.FailureCode);
        Assert.NotEmpty(result.Conflicts);
        Assert.Equal(PortProtocol.Udp, result.Conflicts[0].Protocol);
    }

    [Fact]
    public void Probe_WhenIpv4BindAddressIsNotLocal_ReturnsIpv4BindFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortProbeService();
        int port = workspace.GetAvailableTcpPort();

        PortCheckResult result = service.Probe(new PortCheckRequest(
            port,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            bindAddress: "203.0.113.10",
            displayName: "Java server"));

        Assert.False(result.IsSuccessful);
        Assert.Equal(PortFailureCode.IPv4BindFailure, result.FailureCode);
    }

    [Fact]
    public void Probe_WhenIpv6BindAddressIsNotLocal_ReturnsIpv6BindFailure()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return;
        }

        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortProbeService();
        int port = workspace.GetAvailableTcpPort();

        PortCheckResult result = service.Probe(new PortCheckRequest(
            port,
            PortProtocol.Tcp,
            PortIpMode.IPv6,
            bindAddress: "2001:db8::1234",
            displayName: "Java server"));

        Assert.False(result.IsSuccessful);
        Assert.Equal(PortFailureCode.IPv6BindFailure, result.FailureCode);
    }
}


