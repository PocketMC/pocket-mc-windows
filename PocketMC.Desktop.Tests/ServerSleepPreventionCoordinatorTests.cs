using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Infrastructure.Power;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class ServerSleepPreventionCoordinatorTests
{
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsContinuous = 0x80000000;

    [Fact]
    public void Refresh_WhenSettingEnabledAndServerIsActive_PreventsSleep()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        AddActiveProcess(processManager);
        workspace.AppState.ApplySettings(new AppSettings { KeepComputerAwakeWhileServersRunning = true });
        var api = new RecordingExecutionStateApi();
        var sleepService = CreateSleepService(api);
        using var coordinator = CreateCoordinator(processManager, workspace.AppState, sleepService);

        coordinator.Refresh();

        Assert.True(sleepService.IsActive);
        Assert.Equal(new[] { EsContinuous | EsSystemRequired }, api.Calls);
    }

    [Fact]
    public void Refresh_WhenSettingDisabledAndServerIsActive_ReleasesSleepPrevention()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        AddActiveProcess(processManager);
        workspace.AppState.ApplySettings(new AppSettings { KeepComputerAwakeWhileServersRunning = false });
        var api = new RecordingExecutionStateApi();
        var sleepService = CreateSleepService(api);
        sleepService.PreventSleep();
        api.Calls.Clear();
        using var coordinator = CreateCoordinator(processManager, workspace.AppState, sleepService);

        coordinator.Refresh();

        Assert.False(sleepService.IsActive);
        Assert.Equal(new[] { EsContinuous }, api.Calls);
    }

    [Fact]
    public void Refresh_WhenAllServersStop_ReleasesSleepPrevention()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        Guid instanceId = AddActiveProcess(processManager);
        workspace.AppState.ApplySettings(new AppSettings { KeepComputerAwakeWhileServersRunning = true });
        var api = new RecordingExecutionStateApi();
        var sleepService = CreateSleepService(api);
        using var coordinator = CreateCoordinator(processManager, workspace.AppState, sleepService);
        coordinator.Refresh();
        api.Calls.Clear();

        processManager.ActiveProcesses.TryRemove(instanceId, out _);
        coordinator.Refresh();

        Assert.False(sleepService.IsActive);
        Assert.Equal(new[] { EsContinuous }, api.Calls);
    }

    [Fact]
    public void Refresh_WhenCalledRepeatedly_DoesNotDuplicateExecutionStateCalls()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        AddActiveProcess(processManager);
        workspace.AppState.ApplySettings(new AppSettings { KeepComputerAwakeWhileServersRunning = true });
        var api = new RecordingExecutionStateApi();
        var sleepService = CreateSleepService(api);
        using var coordinator = CreateCoordinator(processManager, workspace.AppState, sleepService);

        coordinator.Refresh();
        coordinator.Refresh();
        coordinator.Refresh();

        Assert.True(sleepService.IsActive);
        Assert.Equal(new[] { EsContinuous | EsSystemRequired }, api.Calls);
    }

    [Fact]
    public void Dispose_WhenActive_ReleasesSleepPrevention()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        AddActiveProcess(processManager);
        workspace.AppState.ApplySettings(new AppSettings { KeepComputerAwakeWhileServersRunning = true });
        var api = new RecordingExecutionStateApi();
        var sleepService = CreateSleepService(api);
        var coordinator = CreateCoordinator(processManager, workspace.AppState, sleepService);
        coordinator.Refresh();
        api.Calls.Clear();

        coordinator.Dispose();

        Assert.False(sleepService.IsActive);
        Assert.Equal(new[] { EsContinuous }, api.Calls);
    }

    private static Guid AddActiveProcess(ServerProcessManager processManager)
    {
        Guid instanceId = Guid.NewGuid();
        Assert.True(processManager.ActiveProcesses.TryAdd(instanceId, null!));
        return instanceId;
    }

    private static SleepPreventionService CreateSleepService(RecordingExecutionStateApi api)
    {
        return new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);
    }

    private static ServerSleepPreventionCoordinator CreateCoordinator(
        ServerProcessManager processManager,
        ApplicationState appState,
        SleepPreventionService sleepService)
    {
        return new ServerSleepPreventionCoordinator(
            processManager,
            appState,
            sleepService,
            NullLogger<ServerSleepPreventionCoordinator>.Instance);
    }

    private sealed class RecordingExecutionStateApi : IExecutionStateApi
    {
        public List<uint> Calls { get; } = new();

        public uint SetThreadExecutionState(uint flags)
        {
            Calls.Add(flags);
            return 1;
        }
    }
}
