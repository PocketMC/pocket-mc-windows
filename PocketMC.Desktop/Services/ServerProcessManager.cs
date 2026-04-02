using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Global singleton managing all active Minecraft server processes.
    /// Thread-safe via ConcurrentDictionary.
    /// </summary>
    public static class ServerProcessManager
    {
        private static readonly JobObject _jobObject = new();
        private static readonly ConcurrentDictionary<Guid, ServerProcess> _activeProcesses = new();
        
        // Auto-Restart Tracking State
        private static readonly ConcurrentDictionary<Guid, int> _consecutiveRestarts = new();
        private static readonly ConcurrentDictionary<Guid, DateTime> _lastStartTime = new();
        private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> _restartCancellations = new();

        /// <summary>
        /// Fires when any instance changes state (started, stopped, crashed).
        /// </summary>
        public static event Action<Guid, ServerState>? OnInstanceStateChanged;

        /// <summary>
        /// Fires every second while a crashed server is waiting to auto-restart.
        /// </summary>
        public static event Action<Guid, int>? OnRestartCountdownTick;

        /// <summary>
        /// Gets the collection of currently active server processes.
        /// </summary>
        public static ConcurrentDictionary<Guid, ServerProcess> ActiveProcesses => _activeProcesses;

        /// <summary>
        /// Starts a server process for the given instance.
        /// Throws if already running, or if java/server.jar missing.
        /// </summary>
        public static ServerProcess StartProcess(InstanceMetadata meta, string appRootPath)
        {
            if (_activeProcesses.ContainsKey(meta.Id))
                throw new InvalidOperationException($"Server '{meta.Name}' is already running.");

            // Reset consecutive restarts if it's been running stably for >10 mins
            if (_lastStartTime.TryGetValue(meta.Id, out var lastStart))
            {
                if ((DateTime.UtcNow - lastStart).TotalMinutes > 10)
                {
                    _consecutiveRestarts[meta.Id] = 0;
                }
            }
            _lastStartTime[meta.Id] = DateTime.UtcNow;

            var serverProcess = new ServerProcess(meta.Id, _jobObject);

            serverProcess.OnStateChanged += (state) =>
            {
                OnInstanceStateChanged?.Invoke(meta.Id, state);

                if (state == ServerState.Stopped || state == ServerState.Crashed)
                {
                    _activeProcesses.TryRemove(meta.Id, out _);
                }
            };
            
            serverProcess.OnServerCrashed += async (crashLog) => 
            {
                await HandleServerCrashAsync(meta, appRootPath);
            };

            serverProcess.Start(meta, appRootPath);
            _activeProcesses[meta.Id] = serverProcess;

            return serverProcess;
        }

        private static async Task HandleServerCrashAsync(InstanceMetadata meta, string appRootPath)
        {
            if (!meta.EnableAutoRestart) return;

            int attempts = _consecutiveRestarts.GetOrAdd(meta.Id, 0);

            if (attempts >= meta.MaxAutoRestarts)
            {
                new ToastContentBuilder()
                    .AddText($"PocketMC Server Crashed")
                    .AddText($"Server '{meta.Name}' has crashed consecutively {attempts} times and hit the max auto-restart limit.")
                    .Show();
                return;
            }

            var cts = new CancellationTokenSource();
            _restartCancellations[meta.Id] = cts;

            try
            {
                for (int i = meta.AutoRestartDelaySeconds; i > 0; i--)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    OnRestartCountdownTick?.Invoke(meta.Id, i);
                    await Task.Delay(1000, cts.Token);
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _restartCancellations.TryRemove(meta.Id, out _);
            }

            if (!cts.IsCancellationRequested)
            {
                _consecutiveRestarts[meta.Id] = attempts + 1;
                StartProcess(meta, appRootPath);
            }
        }

        public static bool IsWaitingToRestart(Guid instanceId)
        {
            return _restartCancellations.ContainsKey(instanceId);
        }

        public static void AbortRestartDelay(Guid instanceId)
        {
            if (_restartCancellations.TryGetValue(instanceId, out var cts))
            {
                cts.Cancel();
                OnInstanceStateChanged?.Invoke(instanceId, ServerState.Crashed); // Fallback to crashed
            }
        }

        /// <summary>
        /// Gracefully stops a running server by sending /stop and waiting.
        /// </summary>
        public static async Task StopProcessAsync(Guid instanceId)
        {
            AbortRestartDelay(instanceId);
            if (_activeProcesses.TryGetValue(instanceId, out var process))
            {
                await process.StopAsync();
            }
        }

        /// <summary>
        /// Force kills a running server immediately.
        /// </summary>
        public static void KillProcess(Guid instanceId)
        {
            AbortRestartDelay(instanceId);
            if (_activeProcesses.TryGetValue(instanceId, out var process))
            {
                process.Kill();
            }
        }

        /// <summary>
        /// Returns whether a specific instance is currently running.
        /// </summary>
        public static bool IsRunning(Guid instanceId)
        {
            return _activeProcesses.ContainsKey(instanceId) &&
                   _activeProcesses[instanceId].State != ServerState.Stopped &&
                   _activeProcesses[instanceId].State != ServerState.Crashed;
        }

        /// <summary>
        /// Gets the ServerProcess for a running instance, or null. 
        /// </summary>
        public static ServerProcess? GetProcess(Guid instanceId)
        {
            _activeProcesses.TryGetValue(instanceId, out var process);
            return process;
        }

        /// <summary>
        /// Kills all running processes. Called on application shutdown.
        /// </summary>
        public static void KillAll()
        {
            foreach (var cts in _restartCancellations.Values) cts.Cancel();
            _restartCancellations.Clear();

            foreach (var kvp in _activeProcesses)
            {
                try { kvp.Value.Kill(); } catch { }
            }
            _activeProcesses.Clear();
        }
    }
}
