using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Features.Instances.Models;

/// <summary>
/// Wraps a single Minecraft server process. 
/// Delegated launch configuration to ServerLaunchConfigurator.
/// </summary>
public class ServerProcess : IDisposable
{
    private static readonly Regex PlayerCountRegex = new(@"There are (\d+) of a max", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private Process? _process;
    private readonly JobObject _jobObject;
    private readonly ServerLaunchConfigurator _launchConfigurator;
    private readonly ILogger<ServerProcess> _logger;
    private bool _disposed;
    private volatile bool _intentionalStop;
    private readonly ConcurrentDictionary<TaskCompletionSource<bool>, Regex> _outputWaiters = new();
    private StreamWriter? _sessionLogWriter;
    private const int MAX_BUFFER_LINES = 5000;

    public Guid InstanceId { get; }
    public ServerState State { get; private set; } = ServerState.Stopped;
    public string WorkingDirectory { get; private set; } = string.Empty;
    public ConcurrentQueue<string> OutputBuffer { get; } = new();
    public int PlayerCount { get; private set; }
    public string? CrashContext { get; private set; }
    public DateTime? StartTime { get; private set; }

    public event Action<string>? OnOutputLine;
    public event Action<string>? OnErrorLine;
    public event Action<int>? OnExited;
    public event Action<ServerState>? OnStateChanged;
    public event Action<string>? OnServerCrashed;

    public ServerProcess(Guid instanceId, JobObject jobObject, ServerLaunchConfigurator launchConfigurator, ILogger<ServerProcess> logger)
    {
        InstanceId = instanceId;
        _jobObject = jobObject;
        _launchConfigurator = launchConfigurator;
        _logger = logger;
    }

    public async Task StartAsync(InstanceMetadata meta, string workingDir, string appRootPath)
    {
        if (State != ServerState.Stopped && State != ServerState.Crashed)
            throw new InvalidOperationException($"Cannot start server — current state is {State}.");

        WorkingDirectory = workingDir;
        InitializeSessionLog(workingDir);
        CleanSessionLock(workingDir);

        var psi = await _launchConfigurator.ConfigureAsync(meta, workingDir, appRootPath, l => AppendOutput(l));

        SetState(ServerState.Starting);
        _intentionalStop = false;

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;
        _process.Start();
        StartTime = DateTime.UtcNow;

        try { _jobObject.AddProcess(_process.Handle); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to assign process to job object."); }

        _ = Task.Run(() => ReadStreamAsync(_process.StandardOutput, false));
        _ = Task.Run(() => ReadStreamAsync(_process.StandardError, true));
    }

    private void InitializeSessionLog(string workingDir)
    {
        try
        {
            string logDir = Path.Combine(workingDir, "logs");
            Directory.CreateDirectory(logDir);
            string sessionLogPath = Path.Combine(logDir, "pocketmc-session.log");
            var stream = new FileStream(sessionLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _sessionLogWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to initialize session log."); }
    }

    private void CleanSessionLock(string workingDir)
    {
        try
        {
            string lockPath = Path.Combine(workingDir, "world", "session.lock");
            if (File.Exists(lockPath))
            {
                _logger.LogInformation("Found stale session.lock for instance {InstanceId}. Cleaning up...", InstanceId);
                File.Delete(lockPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean session.lock for instance {InstanceId}. Launch might fail.", InstanceId);
        }
    }

    public async Task WriteInputAsync(string command)
    {
        if (_process != null && !_process.HasExited)
            await _process.StandardInput.WriteLineAsync(command);
    }

    public async Task StopAsync(int timeoutMs = 15000)
    {
        if (_process == null || _process.HasExited) return;
        _intentionalStop = true;
        SetState(ServerState.Stopping);
        
        bool rconSuccess = await TryStopViaRconAsync(WorkingDirectory);
        if (!rconSuccess)
        {
            await WriteInputAsync("stop");
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        try { await _process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to force-kill after timeout."); }
        }
        SetState(ServerState.Stopped);
    }

    private async Task<bool> TryStopViaRconAsync(string serverDir)
    {
        try
        {
            var propsFile = Path.Combine(serverDir, "server.properties");
            if (!File.Exists(propsFile)) return false;

            var props = PocketMC.Desktop.Features.Instances.ServerPropertiesParser.Read(propsFile);
            if (!props.TryGetValue("enable-rcon", out var rconEnabled) || rconEnabled != "true")
                return false;
                
            if (!props.TryGetValue("rcon.port", out var portStr))
                return false;
                
            if (!int.TryParse(portStr, out int port))
                return false;

            if (!props.TryGetValue("rcon.password", out var password) || string.IsNullOrEmpty(password))
                return false;

            using var rcon = new PocketMC.Desktop.Infrastructure.Process.RconClient("127.0.0.1", port, password);
            await rcon.ConnectAsync();
            await rcon.ExecuteCommandAsync("stop");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send stop via RCON for instance {InstanceId}.", InstanceId);
            return false;
        }
    }

    public void Kill()
    {
        if (_process != null && !_process.HasExited)
        {
            _intentionalStop = true;
            try { _process.Kill(entireProcessTree: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill process."); }
            SetState(ServerState.Stopped);
        }
    }

    private async Task ReadStreamAsync(StreamReader reader, bool isError)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                AppendOutput(line, isError);
                if (!isError)
                {
                    foreach (var kvp in _outputWaiters)
                    {
                        if (kvp.Value.IsMatch(line))
                        {
                            _outputWaiters.TryRemove(kvp.Key, out _);
                            kvp.Key.TrySetResult(true);
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void AppendOutput(string line, bool isError = false)
    {
        string sanitizedLine = LogSanitizer.SanitizeConsoleLine(line);
        OutputBuffer.Enqueue(sanitizedLine);
        if (OutputBuffer.Count > MAX_BUFFER_LINES) OutputBuffer.TryDequeue(out _);

        try { _sessionLogWriter?.WriteLine(sanitizedLine); }
        catch { }

        if (isError) OnErrorLine?.Invoke(sanitizedLine);
        else
        {
            OnOutputLine?.Invoke(sanitizedLine);
            if (State == ServerState.Starting && (sanitizedLine.Contains("Done (") || sanitizedLine.Contains("Server started."))) SetState(ServerState.Online);
            UpdatePlayerCount(sanitizedLine);
        }
    }

    private void UpdatePlayerCount(string line)
    {
        if (line.Contains(" joined the game") || line.Contains("Player connected:")) PlayerCount++;
        else if (line.Contains(" left the game") || line.Contains("Player disconnected:")) { PlayerCount = Math.Max(0, PlayerCount - 1); }
        else if (line.Contains("players online:"))
        {
            var match = PlayerCountRegex.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int count)) PlayerCount = count;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        int exitCode = _process?.ExitCode ?? -1;
        if (!_intentionalStop && exitCode != 0)
        {
            var snapshotLines = OutputBuffer.ToArray().TakeLast(50);
            CrashContext = $"--- CRASH DETECTED (Exit Code: {exitCode}) ---\n" + string.Join(Environment.NewLine, snapshotLines);
            SetState(ServerState.Crashed);
            OnServerCrashed?.Invoke(CrashContext!);
        }
        else SetState(ServerState.Stopped);
        OnExited?.Invoke(exitCode);
    }

    private void SetState(ServerState newState)
    {
        if (State != newState)
        {
            State = newState;
            OnStateChanged?.Invoke(newState);
        }
    }

    public Process? GetInternalProcess() => _process;

    public async Task<bool> WaitForConsoleOutputAsync(Regex regex, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _outputWaiters.TryAdd(tcs, regex);
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() => { _outputWaiters.TryRemove(tcs, out _); tcs.TrySetResult(false); });
        return await tcs.Task;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _sessionLogWriter?.Dispose();
            _sessionLogWriter = null;
            Kill();
            _process?.Dispose();
        }
    }
}
