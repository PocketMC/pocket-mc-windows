using System;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Represents a validated PocketMC claim over a specific local port binding.
/// </summary>
public sealed class PortLease
{
    /// <summary>
    /// Initializes a new port lease.
    /// </summary>
    /// <param name="port">The leased local port.</param>
    /// <param name="protocol">The leased transport protocol.</param>
    /// <param name="ipMode">The leased IP stack mode.</param>
    /// <param name="instanceId">The owning PocketMC instance identifier, if known.</param>
    /// <param name="instanceName">The owning PocketMC instance name, if known.</param>
    /// <param name="instancePath">The owning PocketMC instance path, if known.</param>
    /// <param name="bindAddress">The bind address associated with the lease, if any.</param>
    /// <param name="acquiredAtUtc">The UTC timestamp when the lease was acquired.</param>
    public PortLease(
        int port,
        PortProtocol protocol = PortProtocol.Tcp,
        PortIpMode ipMode = PortIpMode.IPv4,
        Guid? instanceId = null,
        string? instanceName = null,
        string? instancePath = null,
        string? bindAddress = null,
        DateTimeOffset? acquiredAtUtc = null)
    {
        Port = port;
        Protocol = protocol;
        IpMode = ipMode;
        InstanceId = instanceId;
        InstanceName = instanceName;
        InstancePath = instancePath;
        BindAddress = bindAddress;
        AcquiredAtUtc = acquiredAtUtc ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the leased local port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the leased transport protocol.
    /// </summary>
    public PortProtocol Protocol { get; }

    /// <summary>
    /// Gets the leased IP stack mode.
    /// </summary>
    public PortIpMode IpMode { get; }

    /// <summary>
    /// Gets the owning PocketMC instance identifier, when the lease belongs to a known instance.
    /// </summary>
    public Guid? InstanceId { get; }

    /// <summary>
    /// Gets the owning PocketMC instance name, when the lease belongs to a known instance.
    /// </summary>
    public string? InstanceName { get; }

    /// <summary>
    /// Gets the owning PocketMC instance path, when the lease belongs to a known instance.
    /// </summary>
    public string? InstancePath { get; }

    /// <summary>
    /// Gets the configured bind address associated with the lease, if any.
    /// </summary>
    public string? BindAddress { get; }

    /// <summary>
    /// Gets the UTC timestamp when the lease was acquired.
    /// </summary>
    public DateTimeOffset AcquiredAtUtc { get; }
}
