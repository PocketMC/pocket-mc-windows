using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.RemoteControl.Services;

public sealed class RemoteInstanceControlService
{
    private readonly InstanceRegistry _registry;
    private readonly IServerLifecycleService _lifecycleService;

    public RemoteInstanceControlService(
        InstanceRegistry registry,
        IServerLifecycleService lifecycleService)
    {
        _registry = registry;
        _lifecycleService = lifecycleService;
    }

    public async Task<RemoteControlActionResult> StartAsync(Guid instanceId)
    {
        InstanceMetadata? metadata = _registry.GetById(instanceId);
        if (metadata == null)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.NotFound, "Instance was not found.");
        }

        try
        {
            await _lifecycleService.StartAsync(metadata);
            return RemoteControlActionResult.Successful();
        }
        catch (Exception ex)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, ex.Message);
        }
    }

    public async Task<RemoteControlActionResult> StopAsync(Guid instanceId)
    {
        if (_registry.GetById(instanceId) == null)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.NotFound, "Instance was not found.");
        }

        try
        {
            await _lifecycleService.StopAsync(instanceId);
            return RemoteControlActionResult.Successful();
        }
        catch (Exception ex)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, ex.Message);
        }
    }

    public async Task<RemoteControlActionResult> RestartAsync(Guid instanceId)
    {
        if (_registry.GetById(instanceId) == null)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.NotFound, "Instance was not found.");
        }

        try
        {
            await _lifecycleService.RestartAsync(instanceId);
            return RemoteControlActionResult.Successful();
        }
        catch (Exception ex)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, ex.Message);
        }
    }
}
