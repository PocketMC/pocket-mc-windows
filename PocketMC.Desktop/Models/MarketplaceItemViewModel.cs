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
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Application.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Features.Players.Services;

namespace PocketMC.Domain.Models;

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
    private readonly PlayerListParser _playerListParser;
    private readonly ConsoleLogHistoryService _consoleLogHistoryService;
    private readonly ILogger<ServerProcess> _logger;
    private CancellationTokenSource? _startupCts;
    private bool _disposed;
    private volatile bool _intentionalStop;
    private readonly ConcurrentDictionary<TaskCompletionSource<bool>, Regex> _outputWaiters = new();
    private StreamWriter? _sessionLogWriter;
    private const int MAX_BUFFER_LINES = 5000;
    private readonly object _playerListLock = new();
    private List<string> _onlinePlayerNames = new();
    private List<string> _pendingMultilinePlayerNames = new();
    private int? _pendingMultilinePlayerCount;
    private PlayerListContinuationStyle _pendingMultilineStyle = PlayerListContinuationStyle.None;
    private string _serverType = "Vanilla";

    // Health Check Flags
    private bool _hasLoggedMixinWarning;
    private bool _hasLoggedFmlWarning;

    // ── List-command console suppression ──────────────────────────────
    // Programmatic "list" commands flood the console with player-count
    // responses every few seconds.  We suppress the output lines from
    // OnOutputLine (the console UI feed) while keeping ALL internal
    // processing intact (output buffer, session log, player counting).
    // Every Nth response is still shown so the console isn't completely
    // silent about list activity.
    private const int ListSuppressShowEvery = 100;
    private int _pendingAutoListCommands;
    private int _autoListSuppressCounter;
    private bool _suppressingListResponse;

    public Guid InstanceId { get; }
    public ServerState State { get; private set; } = ServerState.Stopped;
    public string WorkingDirectory { get; private set; } = string.Empty;
    public ConcurrentQueue<string> OutputBuffer { get; } = new();
    public int PlayerCount { get; private set; }
    public DateTime? LastPlayerListUpdatedUtc { get; private set; }
    public string? CrashContext { get; private set; }
    public DateTime? StartTime { get; private set; }
    public IReadOnlyList<string> OnlinePlayerNames
    {
        get
        {
            lock (_playerListLock)
            {
                return _onlinePlayerNames.ToArray();
            }
        }
    }

    public event Action<string>? OnOutputLine;
    public event Action<string>? OnErrorLine;
    public event Action<int>? OnExited;
    public event Action<ServerState>? OnStateChanged;
    public event Action<string>? OnServerCrashed;
    public event Action<IReadOnlyList<string>, DateTime>? OnOnlinePlayersUpdated;

    public ServerProcess(
        Guid instanceId,
        JobObject jobObject,
        ServerLaunchConfigurator launchConfigurator,
        PlayerListParser playerListParser,
        ConsoleLogHistoryService consoleLogHistoryService,
        ILogger<ServerProcess> logger)
    {
        InstanceId = instanceId;
        _jobObject = jobObject;
        _launchConfigurator = launchConfigurator;
        _playerListParser = playerListParser;
        _consoleLogHistoryService = consoleLogHistoryService;
        _logger = logger;
    }

    public async Task StartAsync(InstanceMetadata meta, string workingDir, string appRootPath)
    {
        if (State != ServerState.Stopped && State != ServerState.Crashed)
            throw new InvalidOperationException($"Cannot start server — current state is {State}.");

        _serverType = meta.ServerType;
        WorkingDirectory = workingDir;
        CloseSessionLog();
        InitializeSessionLog(workingDir);
        InspectSessionLock(workingDir);

        try
        {
            SetState(ServerState.SettingUp);
            _intentionalStop = false;
            _startupCts = new CancellationTokenSource();

            var psi = await _launchConfigurator.ConfigureAsync(
                meta, workingDir, appRootPath,
                l => AppendOutput(l),
                onStateChange: s => SetState(s),
                cancellationToken: _startupCts.Token);

            // After ConfigureAsync returns (installer and downloads done), transition to Starting
            SetState(ServerState.Starting);

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;
            _process.Start();
            StartTime = DateTime.UtcNow;

            try { _jobObject.AddProcess(_process.Handle); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to assign process to job object."); }

            _ = Task.Run(() => ReadStreamAsync(_process.StandardOutput, false));
            _ = Task.Run(() => ReadStreamAsync(_process.StandardError, true));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Server startup was cancelled.");
            SetState(ServerState.Stopped);
        }
        catch
        {
            CloseSessionLog();
            throw;
        }
    }

    private void InitializeSessionLog(string workingDir)
    {
        try
        {
            _sessionLogWriter = _consoleLogHistoryService.OpenCurrentSessionWriter(workingDir);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to initialize session log."); }
    }

    private void InspectSessionLock(string workingDir)
    {
        try
        {
            string lockPath = Path.Combine(workingDir, "world", "session.lock");
            if (!File.Exists(lockPath))
            {
                return;
            }

            string warning = "[PocketMC] Warning: world/session.lock already exists. PocketMC will not delete it automatically because that can hide an active world lock and risk corruption. If startup fails, stop any other server using this world or remove the stale lock manually only after verifying the world is not in use.";
            _logger.LogWarning("Detected existing Minecraft session.lock for instance {InstanceId} at {LockPath}. PocketMC will not delete it automatically.", InstanceId, lockPath);
            AppendOutput(warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect session.lock for instance {InstanceId}. Launch will continue without deleting lock files.", InstanceId);
        }
    }

    public async Task WriteInputAsync(string command)
    {
        if (_process != null && !_process.HasExited)
            await _process.StandardInput.WriteLineAsync(command);
    }

    /// <summary>
    /// Sends the "list" command to the server and marks the response for
    /// console-display suppression.  All internal processing (player count,
    /// session log, output buffer) continues normally — only the
    /// <see cref="OnOutputLine"/> event is suppressed for the response lines.
    /// Every <see cref="ListSuppressShowEvery"/>th response is still emitted
    /// so the user knows the feature is active.
    /// </summary>
    public async Task WriteListCommandAsync()
    {
        Interlocked.Increment(ref _pendingAutoListCommands);
        await WriteInputAsync("list");
    }

    public async Task StopAsync(int timeoutMs = 15000)
    {
        if (State == ServerState.Stopped || State == ServerState.Stopping) return;
        _intentionalStop = true;
        SetState(ServerState.Stopping);
        _startupCts?.Cancel();

        if (_process == null || _process.HasExited)
        {
            SetState(ServerState.Stopped);
            return;
        }

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
        CloseSessionLog();
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
            CloseSessionLog();
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
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Server process stream closed for instance {InstanceId}.", InstanceId);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Server process stream ended for instance {InstanceId}.", InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Server process stream reader failed for instance {InstanceId}. IsErrorStream={IsErrorStream}", InstanceId, isError);
        }
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
            // Player-count processing ALWAYS runs regardless of suppression.
            if (State == ServerState.Starting)
            {
                if (sanitizedLine.Contains("Done (") || sanitizedLine.Contains("Server started."))
                {
                    SetState(ServerState.Online);
                }
                else if ((sanitizedLine.Contains("org.spongepowered.asm.mixin.") || sanitizedLine.Contains("cpw.mods.modlauncher") || sanitizedLine.Contains("Found mod file")) && !_hasLoggedMixinWarning)
                {
                    _hasLoggedMixinWarning = true;
                    OnOutputLine?.Invoke("[PocketMC Health Check] Forge is injecting Mixins and discovering mods. This is a CPU intensive phase and may take several minutes. The server is NOT frozen.");
                }
                else if ((sanitizedLine.Contains("Forge Mod Loader") || sanitizedLine.Contains("FML")) && sanitizedLine.Contains("preinitialization") && !_hasLoggedFmlWarning)
                {
                    _hasLoggedFmlWarning = true;
                    OnOutputLine?.Invoke("[PocketMC Health Check] FML Pre-Initialization started. Mod loading can take a significant amount of time depending on the modpack size...");
                }
            }

            UpdatePlayerCount(sanitizedLine);

            // Determine if this line should be hidden from the console UI.
            if (!ShouldSuppressListLine(sanitizedLine))
            {
                OnOutputLine?.Invoke(sanitizedLine);
            }
        }
    }

    /// <summary>
    /// Returns true when the line is part of an auto-generated "list"
    /// command response and should be hidden from the console display.
    /// </summary>
    private bool ShouldSuppressListLine(string line)
    {
        // If we're in the middle of suppressing multi-line continuation,
        // keep suppressing until the multi-line parse completes.
        if (_suppressingListResponse && _pendingMultilinePlayerCount.HasValue)
        {
            return true;
        }

        // Check if this is a list-response header line.
        bool isListResponse = IsListResponseLine(line);

        if (isListResponse && Volatile.Read(ref _pendingAutoListCommands) > 0)
        {
            Interlocked.Decrement(ref _pendingAutoListCommands);
            _autoListSuppressCounter++;

            if (_autoListSuppressCounter >= ListSuppressShowEvery)
            {
                // Let this one through so the console shows periodic proof of life.
                _autoListSuppressCounter = 0;
                _suppressingListResponse = false;
                return false;
            }

            // Suppress this response (and any continuation lines).
            _suppressingListResponse = true;
            return true;
        }

        // Not a list response — clear the suppression flag.
        _suppressingListResponse = false;
        return false;
    }

    /// <summary>
    /// Detects whether a line is the header of a "list" command response
    /// across all supported server types (Java, Bedrock, PocketMine).
    /// </summary>
    internal static bool IsListResponseLine(string line)
    {
        // Fast path: all known list-response formats contain one of these.
        return line.Contains("players online", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Players connected", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Online players", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdatePlayerCount(string line)
    {
        if (_pendingMultilinePlayerCount.HasValue)
        {
            if (_playerListParser.TryParseContinuationLine(line, _pendingMultilineStyle, out string playerName))
            {
                _pendingMultilinePlayerNames.Add(playerName);
                if (_pendingMultilinePlayerNames.Count >= _pendingMultilinePlayerCount.Value)
                {
                    CommitOnlinePlayers(_pendingMultilinePlayerNames);
                    _pendingMultilinePlayerCount = null;
                    _pendingMultilinePlayerNames = new List<string>();
                    _pendingMultilineStyle = PlayerListContinuationStyle.None;
                }

                return;
            }

            if (_pendingMultilineStyle == PlayerListContinuationStyle.BedrockPlainNames)
            {
                CommitOnlinePlayers(_pendingMultilinePlayerNames);
            }

            _pendingMultilinePlayerCount = null;
            _pendingMultilinePlayerNames = new List<string>();
            _pendingMultilineStyle = PlayerListContinuationStyle.None;
        }

        PlayerListParseResult? parseResult = _playerListParser.ParseLine(line, _serverType);
        if (parseResult != null)
        {
            PlayerCount = parseResult.OnlinePlayerCount;
            if (parseResult.IsComplete)
            {
                CommitOnlinePlayers(parseResult.OnlinePlayerNames);
            }
            else
            {
                _pendingMultilinePlayerCount = parseResult.OnlinePlayerCount;
                _pendingMultilinePlayerNames = new List<string>();
                _pendingMultilineStyle = parseResult.ContinuationStyle;
            }

            return;
        }

        if (line.Contains(" joined the game") || line.Contains("Player connected:")) PlayerCount++;
        else if (line.Contains(" left the game") || line.Contains("Player disconnected:")) { PlayerCount = Math.Max(0, PlayerCount - 1); }
        else if (line.Contains("players online:"))
        {
            var match = PlayerCountRegex.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int count)) PlayerCount = count;
        }
    }

    private void CommitOnlinePlayers(IReadOnlyList<string> playerNames)
    {
        List<string> snapshot = playerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(PlayerListParser.NormalizePlayerName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DateTime updatedAt = DateTime.UtcNow;
        lock (_playerListLock)
        {
            _onlinePlayerNames = snapshot;
            LastPlayerListUpdatedUtc = updatedAt;
        }

        OnOnlinePlayersUpdated?.Invoke(snapshot, updatedAt);
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
        CloseSessionLog();
        OnExited?.Invoke(exitCode);
    }

    private void CloseSessionLog()
    {
        try { _sessionLogWriter?.Dispose(); }
        catch { }
        finally { _sessionLogWriter = null; }
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
            CloseSessionLog();
            Kill();
            _process?.Dispose();
        }
    }
}

