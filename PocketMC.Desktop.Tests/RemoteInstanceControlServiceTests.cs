using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl;

#pragma warning disable CS0067
public sealed class RemoteInstanceControlServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_DelegatesToLifecycleForExistingInstance()
    {
        var metadata = new InstanceMetadata { Id = Guid.NewGuid(), Name = "Survival" };
        var lifecycle = new FakeLifecycleService();
        var service = CreateService(metadata, lifecycle);

        RemoteControlActionResult result = await service.StartAsync(metadata.Id);

        Assert.True(result.Success);
        Assert.Equal(metadata.Id, lifecycle.StartedInstanceId);
    }

    [Fact]
    public async Task RestartAsync_ReturnsNotFoundForUnknownInstance()
    {
        var metadata = new InstanceMetadata { Id = Guid.NewGuid(), Name = "Survival" };
        var lifecycle = new FakeLifecycleService();
        var service = CreateService(metadata, lifecycle);

        RemoteControlActionResult result = await service.RestartAsync(Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(RemoteControlActionFailure.NotFound, result.Failure);
        Assert.Null(lifecycle.RestartedInstanceId);
    }

    private RemoteInstanceControlService CreateService(InstanceMetadata metadata, FakeLifecycleService lifecycle)
    {
        Directory.CreateDirectory(_tempDirectory);
        var state = new ApplicationState();
        state.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });
        var registry = new InstanceRegistry(new InstancePathService(state), NullLogger<InstanceRegistry>.Instance);
        string instancePath = Path.Combine(_tempDirectory, "servers", metadata.Name);
        Directory.CreateDirectory(instancePath);
        registry.Register(metadata, instancePath);

        return new RemoteInstanceControlService(registry, lifecycle);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class FakeLifecycleService : IServerLifecycleService
    {
        public Guid? StartedInstanceId { get; private set; }
        public Guid? StoppedInstanceId { get; private set; }
        public Guid? RestartedInstanceId { get; private set; }

        public event Action<Guid, ServerState>? OnInstanceStateChanged;
        public event Action<Guid, int>? OnRestartCountdownTick;

        public Task StartAsync(InstanceMetadata meta)
        {
            StartedInstanceId = meta.Id;
            return Task.CompletedTask;
        }

        public Task StopAsync(Guid instanceId)
        {
            StoppedInstanceId = instanceId;
            return Task.CompletedTask;
        }

        public void Kill(Guid instanceId) { }
        public void KillAll() { }
        public bool IsRunning(Guid instanceId) => false;
        public bool IsWaitingToRestart(Guid instanceId) => false;
        public void AbortRestartDelay(Guid instanceId) { }

        public Task RestartAsync(Guid instanceId)
        {
            RestartedInstanceId = instanceId;
            return Task.CompletedTask;
        }

        public ServerProcess? GetProcess(Guid instanceId) => null;
        public DateTime? GetSessionStartTime(Guid instanceId) => null;
        public Task ReleaseInstanceAsync(Guid instanceId) => Task.CompletedTask;
    }
}
#pragma warning restore CS0067



