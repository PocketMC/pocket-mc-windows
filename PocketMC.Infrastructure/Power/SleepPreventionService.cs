using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Power;

public interface IExecutionStateApi
{
    uint SetThreadExecutionState(uint flags);
}

public sealed class Kernel32ExecutionStateApi : IExecutionStateApi
{
    public uint SetThreadExecutionState(uint flags) => SetThreadExecutionStateNative(flags);

    [DllImport("kernel32.dll", EntryPoint = "SetThreadExecutionState", SetLastError = true)]
    private static extern uint SetThreadExecutionStateNative(uint flags);
}

public sealed class SleepPreventionService : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000
    }

    private readonly IExecutionStateApi _executionStateApi;
    private readonly ILogger<SleepPreventionService> _logger;
    private readonly object _gate = new();
    private readonly BlockingCollection<Action> _requests = new();
    private readonly Thread _ownerThread;
    private bool _isActive;
    private bool _disposed;

    public SleepPreventionService(
        IExecutionStateApi executionStateApi,
        ILogger<SleepPreventionService> logger)
    {
        _executionStateApi = executionStateApi;
        _logger = logger;
        _ownerThread = new Thread(() => { foreach (var request in _requests.GetConsumingEnumerable()) request(); }) { IsBackground = true, Name = "PocketMC power requests" };
        _ownerThread.Start();
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _isActive;
            }
        }
    }

    public void PreventSleep()
    {
        lock (_gate)
        {
            if (_disposed || _isActive)
            {
                return;
            }

            var flags = ExecutionState.ES_CONTINUOUS | ExecutionState.ES_SYSTEM_REQUIRED;
            if (!TrySetExecutionState(flags, "prevent Windows automatic sleep"))
            {
                return;
            }

            _isActive = true;
        }
    }

    public void AllowSleep()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ReleaseIfActive();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            ReleaseIfActive();
            _disposed = true;
            _requests.CompleteAdding();
            _ownerThread.Join();
            _requests.Dispose();
        }
    }

    private void ReleaseIfActive()
    {
        if (!_isActive)
        {
            return;
        }

        if (!TrySetExecutionState(ExecutionState.ES_CONTINUOUS, "release Windows automatic sleep prevention"))
        {
            return;
        }

        _isActive = false;
    }

    private bool TrySetExecutionState(ExecutionState flags, string operation)
    {
        var completed = new TaskCompletionSource<(uint Result, int Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Add(() =>
        {
            try
            {
                uint result = _executionStateApi.SetThreadExecutionState((uint)flags);
                completed.SetResult((result, Marshal.GetLastWin32Error()));
            }
            catch (Exception ex) { completed.SetException(ex); }
        });
        var response = completed.Task.GetAwaiter().GetResult();
        uint result = response.Result;
        if (result != 0)
        {
            return true;
        }

        int errorCode = response.Error;
        _logger.LogWarning(
            "SetThreadExecutionState failed while attempting to {Operation}. Flags={Flags}; LastWin32Error={ErrorCode}.",
            operation,
            flags,
            errorCode);
        return false;
    }
}
