using PocketMC.Application.Services.Networking;
using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace PocketMC.Infrastructure.Networking;

/// <summary>
/// Stores in-memory port leases for PocketMC instances to prevent startup-time reservation races.
/// </summary>
public sealed class PortLeaseRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<PortLeaseKey, PortLease> _leasesByKey = new();
    private readonly Dictionary<Guid, HashSet<PortLeaseKey>> _leaseKeysByInstance = new();

    /// <summary>
    /// Attempts to reserve a lease for the supplied instance.
    /// </summary>
    /// <param name="lease">The lease to reserve.</param>
    /// <param name="conflictingLease">When the reservation fails, receives the conflicting lease.</param>
    /// <returns><see langword="true"/> when the lease is reserved or already reserved by the same instance; otherwise <see langword="false"/>.</returns>
    public bool TryReserve(PortLease lease, out PortLease? conflictingLease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (!lease.InstanceId.HasValue)
        {
            throw new ArgumentException("Port leases stored in the registry must have an owning instance id.", nameof(lease));
        }

        PortLeaseKey key = PortLeaseKey.FromLease(lease);

        lock (_sync)
        {
            if (_leasesByKey.TryGetValue(key, out PortLease? existingExactLease))
            {
                if (existingExactLease.InstanceId == lease.InstanceId)
                {
                    conflictingLease = null;
                    return true;
                }

                conflictingLease = existingExactLease;
                return false;
            }

            PortLease? overlap = FindOverlappingLeaseUnsafe(lease);
            if (overlap != null)
            {
                conflictingLease = overlap;
                return false;
            }

            _leasesByKey[key] = lease;

            Guid instanceId = lease.InstanceId.Value;
            if (!_leaseKeysByInstance.TryGetValue(instanceId, out HashSet<PortLeaseKey>? keys))
            {
                keys = new HashSet<PortLeaseKey>();
                _leaseKeysByInstance[instanceId] = keys;
            }

            keys.Add(key);
            conflictingLease = null;
            return true;
        }
    }

    /// <summary>
    /// Releases a previously reserved exact lease.
    /// </summary>
    /// <param name="lease">The lease to release.</param>
    /// <returns><see langword="true"/> when the lease was removed; otherwise <see langword="false"/>.</returns>
    public bool Release(PortLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (!lease.InstanceId.HasValue)
        {
            return false;
        }

        return Release(
            lease.InstanceId.Value,
            lease.Port,
            lease.Protocol,
            lease.IpMode,
            lease.BindAddress);
    }

    /// <summary>
    /// Releases a previously reserved exact lease for the supplied instance.
    /// </summary>
    /// <param name="instanceId">The owning instance id.</param>
    /// <param name="port">The leased port.</param>
    /// <param name="protocol">The leased protocol.</param>
    /// <param name="ipMode">The leased IP mode.</param>
    /// <param name="bindAddress">The leased bind address, if any.</param>
    /// <returns><see langword="true"/> when the lease was removed; otherwise <see langword="false"/>.</returns>
    public bool Release(Guid instanceId, int port, PortProtocol protocol, PortIpMode ipMode, string? bindAddress = null)
    {
        PortLeaseKey key = PortLeaseKey.FromValues(port, protocol, ipMode, bindAddress);

        lock (_sync)
        {
            if (!_leasesByKey.TryGetValue(key, out PortLease? existing) || existing.InstanceId != instanceId)
            {
                return false;
            }

            _leasesByKey.Remove(key);
            RemoveKeyFromInstanceIndexUnsafe(instanceId, key);
            return true;
        }
    }

    /// <summary>
    /// Releases every lease currently held by the supplied instance.
    /// </summary>
    /// <param name="instanceId">The instance whose leases should be released.</param>
    /// <returns>The number of leases removed.</returns>
    public int ReleaseInstance(Guid instanceId)
    {
        lock (_sync)
        {
            if (!_leaseKeysByInstance.TryGetValue(instanceId, out HashSet<PortLeaseKey>? keys) || keys.Count == 0)
            {
                return 0;
            }

            int removed = 0;
            foreach (PortLeaseKey key in keys.ToArray())
            {
                if (_leasesByKey.Remove(key))
                {
                    removed++;
                }
            }

            _leaseKeysByInstance.Remove(instanceId);
            return removed;
        }
    }

    /// <summary>
    /// Finds the currently held lease that would conflict with the supplied lease request.
    /// </summary>
    /// <param name="lease">The lease request to check.</param>
    /// <returns>The conflicting lease, or <see langword="null"/> when no matching holder exists.</returns>
    public PortLease? FindHolder(PortLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        lock (_sync)
        {
            return FindOverlappingLeaseUnsafe(lease);
        }
    }

    /// <summary>
    /// Finds the currently held lease that would conflict with the supplied reservation tuple.
    /// </summary>
    /// <param name="port">The port to check.</param>
    /// <param name="protocol">The protocol to check.</param>
    /// <param name="ipMode">The IP mode to check.</param>
    /// <param name="bindAddress">The bind address to check, if any.</param>
    /// <returns>The conflicting lease, or <see langword="null"/> when no matching holder exists.</returns>
    public PortLease? FindHolder(int port, PortProtocol protocol, PortIpMode ipMode, string? bindAddress = null)
    {
        lock (_sync)
        {
            PortLease probe = new(
                port,
                protocol,
                ipMode,
                instanceId: null,
                instanceName: null,
                instancePath: null,
                bindAddress: bindAddress,
                acquiredAtUtc: DateTimeOffset.MinValue);

            return FindOverlappingLeaseUnsafe(probe);
        }
    }

    /// <summary>
    /// Returns a snapshot of all leases currently held by the supplied instance.
    /// </summary>
    /// <param name="instanceId">The instance whose leases should be returned.</param>
    /// <returns>A stable snapshot of the instance's current leases.</returns>
    public IReadOnlyList<PortLease> GetLeasesForInstance(Guid instanceId)
    {
        lock (_sync)
        {
            if (!_leaseKeysByInstance.TryGetValue(instanceId, out HashSet<PortLeaseKey>? keys) || keys.Count == 0)
            {
                return Array.Empty<PortLease>();
            }

            return keys
                .Select(key => _leasesByKey[key])
                .OrderBy(lease => lease.Port)
                .ThenBy(lease => lease.Protocol)
                .ThenBy(lease => lease.IpMode)
                .ToArray();
        }
    }

    /// <summary>
    /// Returns a snapshot of every lease currently tracked by the registry.
    /// </summary>
    /// <returns>A stable snapshot of all tracked leases.</returns>
    public IReadOnlyList<PortLease> GetAllLeases()
    {
        lock (_sync)
        {
            return _leasesByKey.Values
                .OrderBy(lease => lease.Port)
                .ThenBy(lease => lease.Protocol)
                .ThenBy(lease => lease.IpMode)
                .ToArray();
        }
    }

    private PortLease? FindOverlappingLeaseUnsafe(PortLease candidate)
    {
        foreach (PortLease existing in _leasesByKey.Values)
        {
            if (LeasesOverlap(existing, candidate))
            {
                return existing;
            }
        }

        return null;
    }

    private void RemoveKeyFromInstanceIndexUnsafe(Guid instanceId, PortLeaseKey key)
    {
        if (!_leaseKeysByInstance.TryGetValue(instanceId, out HashSet<PortLeaseKey>? keys))
        {
            return;
        }

        keys.Remove(key);
        if (keys.Count == 0)
        {
            _leaseKeysByInstance.Remove(instanceId);
        }
    }

    private static bool LeasesOverlap(PortLease left, PortLease right)
    {
        return left.Port == right.Port &&
               ProtocolsOverlap(left.Protocol, right.Protocol) &&
               IpModesOverlap(left.IpMode, right.IpMode) &&
               BindAddressesOverlap(left.BindAddress, right.BindAddress);
    }

    private static bool ProtocolsOverlap(PortProtocol left, PortProtocol right)
    {
        return left == PortProtocol.TcpAndUdp ||
               right == PortProtocol.TcpAndUdp ||
               left == right;
    }

    private static bool IpModesOverlap(PortIpMode left, PortIpMode right)
    {
        return left == PortIpMode.DualStack ||
               right == PortIpMode.DualStack ||
               left == right;
    }

    private static bool BindAddressesOverlap(string? left, string? right)
    {
        string? normalizedLeft = NormalizeBindAddress(left);
        string? normalizedRight = NormalizeBindAddress(right);

        if (normalizedLeft == null || normalizedRight == null)
        {
            return true;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(normalizedLeft, out IPAddress? leftAddress) ||
            !IPAddress.TryParse(normalizedRight, out IPAddress? rightAddress))
        {
            return false;
        }

        return leftAddress.Equals(rightAddress) ||
               leftAddress.Equals(IPAddress.Any) ||
               leftAddress.Equals(IPAddress.IPv6Any) ||
               rightAddress.Equals(IPAddress.Any) ||
               rightAddress.Equals(IPAddress.IPv6Any);
    }

    private static string? NormalizeBindAddress(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return null;
        }

        string trimmed = bindAddress.Trim();
        return IPAddress.TryParse(trimmed, out IPAddress? parsed)
            ? parsed.ToString()
            : trimmed;
    }

    private readonly record struct PortLeaseKey(
        int Port,
        PortProtocol Protocol,
        PortIpMode IpMode,
        string? BindAddress)
    {
        public static PortLeaseKey FromLease(PortLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            return FromValues(lease.Port, lease.Protocol, lease.IpMode, lease.BindAddress);
        }

        public static PortLeaseKey FromValues(int port, PortProtocol protocol, PortIpMode ipMode, string? bindAddress)
        {
            return new PortLeaseKey(port, protocol, ipMode, NormalizeBindAddress(bindAddress));
        }
    }
}
