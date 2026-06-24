using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Infrastructure.Power;

namespace PocketMC.Desktop.Tests;

public sealed class SleepPreventionServiceTests
{
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;
    private const uint EsContinuous = 0x80000000;

    [Fact]
    public void PreventSleep_WhenInactive_CallsSystemRequiredContinuousOnce()
    {
        var api = new RecordingExecutionStateApi();
        var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);

        service.PreventSleep();

        uint flags = Assert.Single(api.Calls);
        Assert.Equal(EsContinuous | EsSystemRequired, flags);
        Assert.Equal(0u, flags & EsDisplayRequired);
        Assert.True(service.IsActive);
    }

    [Fact]
    public void PreventSleep_WhenAlreadyActive_DoesNotDuplicateExecutionStateCall()
    {
        var api = new RecordingExecutionStateApi();
        var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);

        service.PreventSleep();
        service.PreventSleep();

        Assert.Single(api.Calls);
        Assert.True(service.IsActive);
    }

    [Fact]
    public void AllowSleep_WhenActive_ReleasesContinuousRequest()
    {
        var api = new RecordingExecutionStateApi();
        var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);
        service.PreventSleep();
        api.Calls.Clear();

        service.AllowSleep();

        Assert.Equal(new[] { EsContinuous }, api.Calls);
        Assert.False(service.IsActive);
    }

    [Fact]
    public void AllowSleep_WhenInactive_DoesNotCallExecutionStateApi()
    {
        var api = new RecordingExecutionStateApi();
        var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);

        service.AllowSleep();

        Assert.Empty(api.Calls);
        Assert.False(service.IsActive);
    }

    [Fact]
    public void Dispose_WhenActive_ReleasesSleepPrevention()
    {
        var api = new RecordingExecutionStateApi();
        var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);
        service.PreventSleep();
        api.Calls.Clear();

        service.Dispose();

        Assert.Equal(new[] { EsContinuous }, api.Calls);
        Assert.False(service.IsActive);
    }

    [Fact]
    public void PreventSleep_WhenApiFails_DoesNotMarkServiceActive()
    {
        var api = new RecordingExecutionStateApi { ReturnValue = 0 };
        var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);

        service.PreventSleep();

        Assert.False(service.IsActive);
    }

    private sealed class RecordingExecutionStateApi : IExecutionStateApi
    {
        public List<uint> Calls { get; } = new();
        public uint ReturnValue { get; init; } = 1;

        public uint SetThreadExecutionState(uint flags)
        {
            Calls.Add(flags);
            return ReturnValue;
        }
    }
}


