using PocketMC.Infrastructure.Networking;
using PocketMC.Infrastructure.Tests.TestSupport.Fixtures;
using PocketMC.Domain.Models;
using System.Collections.Concurrent;

namespace PocketMC.Infrastructure.Tests.Networking;

public sealed class PortLeaseRegistryTests
{
    [Fact]
    public void TryReserve_FirstLeaseSucceeds_AndIsQueryable()
    {
        var registry = new PortLeaseRegistry();
        var lease = CreateLease(Guid.NewGuid(), 25565, PortProtocol.Tcp, PortIpMode.DualStack);

        bool acquired = registry.TryReserve(lease, out PortLease? conflict);

        Assert.True(acquired);
        Assert.Null(conflict);

        PortLease? holder = registry.FindHolder(lease);
        Assert.NotNull(holder);
        Assert.Equal(lease.InstanceId, holder.InstanceId);
        Assert.Single(registry.GetLeasesForInstance(lease.InstanceId!.Value));
    }

    [Fact]
    public void TryReserve_SameExactLeaseForSameInstance_IsIdempotent()
    {
        var registry = new PortLeaseRegistry();
        Guid instanceId = Guid.NewGuid();
        var lease = CreateLease(instanceId, 25565, PortProtocol.Tcp, PortIpMode.DualStack);

        Assert.True(registry.TryReserve(lease, out _));
        bool reacquired = registry.TryReserve(lease, out PortLease? conflict);

        Assert.True(reacquired);
        Assert.Null(conflict);
        Assert.Single(registry.GetLeasesForInstance(instanceId));
        Assert.Single(registry.GetAllLeases());
    }

    [Fact]
    public void TryReserve_ConflictingLeaseForDifferentInstance_Fails()
    {
        var registry = new PortLeaseRegistry();
        var first = CreateLease(Guid.NewGuid(), 19132, PortProtocol.Udp, PortIpMode.IPv4);
        var second = CreateLease(Guid.NewGuid(), 19132, PortProtocol.Udp, PortIpMode.IPv4);

        Assert.True(registry.TryReserve(first, out _));

        bool acquired = registry.TryReserve(second, out PortLease? conflict);

        Assert.False(acquired);
        Assert.NotNull(conflict);
        Assert.Equal(first.InstanceId, conflict.InstanceId);
    }

    [Fact]
    public void ReleaseInstance_RemovesAllLeasesForTheInstance()
    {
        var registry = new PortLeaseRegistry();
        Guid firstInstance = Guid.NewGuid();
        Guid secondInstance = Guid.NewGuid();

        Assert.True(registry.TryReserve(CreateLease(firstInstance, 25565, PortProtocol.Tcp, PortIpMode.DualStack), out _));
        Assert.True(registry.TryReserve(CreateLease(firstInstance, 19132, PortProtocol.Udp, PortIpMode.IPv4), out _));
        Assert.True(registry.TryReserve(CreateLease(secondInstance, 25575, PortProtocol.Tcp, PortIpMode.IPv4), out _));

        int removed = registry.ReleaseInstance(firstInstance);

        Assert.Equal(2, removed);
        Assert.Empty(registry.GetLeasesForInstance(firstInstance));
        Assert.Single(registry.GetLeasesForInstance(secondInstance));
        Assert.Single(registry.GetAllLeases());
    }

    [Fact]
    public void Release_RemovesOnlyTheExactLease()
    {
        var registry = new PortLeaseRegistry();
        Guid instanceId = Guid.NewGuid();
        var tcpLease = CreateLease(instanceId, 25565, PortProtocol.Tcp, PortIpMode.DualStack);
        var udpLease = CreateLease(instanceId, 19132, PortProtocol.Udp, PortIpMode.IPv4);

        Assert.True(registry.TryReserve(tcpLease, out _));
        Assert.True(registry.TryReserve(udpLease, out _));

        bool released = registry.Release(tcpLease);

        Assert.True(released);
        Assert.Null(registry.FindHolder(tcpLease));
        Assert.NotNull(registry.FindHolder(udpLease));
        Assert.Single(registry.GetLeasesForInstance(instanceId));
    }

    [Fact]
    public async Task TryReserve_IsThreadSafeForConflictingRequests()
    {
        var registry = new PortLeaseRegistry();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new ConcurrentBag<bool>();

        Task[] tasks = Enumerable.Range(0, 8)
            .Select(async i =>
            {
                await gate.Task;
                bool acquired = registry.TryReserve(
                    CreateLease(Guid.NewGuid(), 25565, PortProtocol.Tcp, PortIpMode.DualStack),
                    out _);
                results.Add(acquired);
            })
            .ToArray();

        gate.SetResult(true);
        await Task.WhenAll(tasks);

        Assert.Single(results.Where(x => x));
        Assert.Single(registry.GetAllLeases());
    }

    private static PortLease CreateLease(Guid instanceId, int port, PortProtocol protocol, PortIpMode ipMode, string? bindAddress = null)
    {
        return new PortLease(
            port,
            protocol,
            ipMode,
            instanceId,
            $"Instance-{instanceId:N}",
            instancePath: $@"F:\PocketMC\servers\{instanceId:N}",
            bindAddress: bindAddress);
    }
}