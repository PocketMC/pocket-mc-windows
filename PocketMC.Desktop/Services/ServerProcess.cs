using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Represents the lifecycle state of a managed Minecraft server.
    /// </summary>
    public enum ServerState
    {
        Stopped,
        Starting,
        Online,
        Stopping,
        Crashed
    }

    /// <summary>
    /// Wraps a single Minecraft server process with strict ProcessStartInfo configuration.
    /// No shell intermediaries (cmd.exe, PowerShell) are used.
    /// </summary>
    public class ServerProcess : IDisposable
    {
        private static readonly Regex PlayerCountRegex = new(
            @"There are (\d+) of a max",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private static readonly Regex AdvancedJvmArgTokenRegex = new(
            "\"[^\"]*\"|\\S+",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private Process? _process;
        private readonly JobObject _jobObject;
        private readonly JavaProvisioningService _javaProvisioning;
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

        public event Action<string>? OnOutputLine;
        public event Action<string>? OnErrorLine;
        public event Action<int>? OnExited;
        public event Action<ServerState>? OnStateChanged;
        public event Action<string>? OnServerCrashed;

        public Process? GetInternalProcess() => _process;

        public ServerProcess(Guid instanceId, JobObject jobObject, JavaProvisioningService javaProvisioning, ILogger<ServerProcess> logger)
        {
            InstanceId = instanceId;
            _jobObject = jobObject;
            _javaProvisioning = javaProvisioning;
            _logger = logger;
        }

        public async Task StartAsync(InstanceMetadata meta, string workingDir, string appRootPath)
        {
            if (State != ServerState.Stopped && State != ServerState.Crashed)
                throw new InvalidOperationException($"Cannot start server — current state is {State}.");

            int requiredJavaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(meta.MinecraftVersion);
            string? bundledJavaPath = JavaRuntimeResolver.GetBundledJavaPath(appRootPath, requiredJavaVersion);
            string javaPath = JavaRuntimeResolver.ResolveJavaPath(meta, appRootPath);

            if (!string.IsNullOrWhiteSpace(meta.CustomJavaPath) && !File.Exists(meta.CustomJavaPath))
            {
                _logger.LogWarning(
                    "Custom Java path {CustomJavaPath} for instance {InstanceId} does not exist. Falling back to the bundled runtime selection.",
                    meta.CustomJavaPath,
                    InstanceId);
            }

            if (javaPath == "java")
            {
                _logger.LogWarning(
                    "Bundled Java {JavaVersion} was not found for Minecraft {MinecraftVersion}. Falling back to system java.",
                    requiredJavaVersion,
                    meta.MinecraftVersion);
            }
            else if (bundledJavaPath != null && string.Equals(javaPath, bundledJavaPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Using bundled Java {JavaVersion} for Minecraft {MinecraftVersion} at {JavaPath}.",
                    requiredJavaVersion,
                    meta.MinecraftVersion,
                    javaPath);
            }

            if (string.IsNullOrWhiteSpace(workingDir))
            {
                throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
            }

            WorkingDirectory = workingDir;

            // Initialize session log
            try
            {
                string logDir = Path.Combine(workingDir, "logs");
                Directory.CreateDirectory(logDir);
                string sessionLogPath = Path.Combine(logDir, "pocketmc-session.log");
                var stream = new FileStream(sessionLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _sessionLogWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize the session log for instance {InstanceId}.", InstanceId);
            }

            string serverJar = Path.Combine(workingDir, "server.jar");
            string forgeInstaller = Path.Combine(workingDir, "forge-installer.jar");
            
            // Check for Forge auto-installation (Arch and Stability improvement)
            if (meta.ServerType == "Forge" && File.Exists(forgeInstaller) && !Directory.Exists(Path.Combine(workingDir, "libraries")))
            {
                SetState(ServerState.Starting);
                AppendOutput("[PocketMC] First-time Forge setup detected. Running installer...");
                
                var installerPsi = new ProcessStartInfo {
                    FileName = javaPath,
                    WorkingDirectory = workingDir,
                    Arguments = "-jar forge-installer.jar --installServer",
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                };

                using var proc = Process.Start(installerPsi);
                if (proc != null) {
                    await Task.Run(() => ReadStreamAsync(proc.StandardOutput, false));
                    await Task.Run(() => ReadStreamAsync(proc.StandardError, false));
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0) AppendOutput("[PocketMC] Forge installation successful.");
                    else throw new Exception($"Forge installer failed with exit code {proc.ExitCode}");
                }
            }

            // Architecture: Ensure required Java runtime is present and healthy (Auto-Repair)
            if (string.IsNullOrWhiteSpace(meta.CustomJavaPath))
            {
                if (!_javaProvisioning.IsJavaVersionPresent(requiredJavaVersion))
                {
                    AppendOutput($"[PocketMC] Required Java {requiredJavaVersion} is missing or corrupt. Starting auto-repair...");
                    try
                    {
                        await _javaProvisioning.EnsureJavaAsync(requiredJavaVersion);
                        AppendOutput($"[PocketMC] Java {requiredJavaVersion} repaired successfully.");
                        // Re-resolve javaPath after repair
                        javaPath = JavaRuntimeResolver.ResolveJavaPath(meta, appRootPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Java auto-repair failed for instance {InstanceId}.", InstanceId);
                        AppendOutput($"[PocketMC] CRITICAL: Java auto-repair failed: {ex.Message}", true);
                        throw;
                    }
                }
            }

            if (!File.Exists(serverJar) && meta.ServerType != "Forge")
            {
                throw new FileNotFoundException($"server.jar not found in:\n{workingDir}");
            }



            var minRamMb = Math.Max(128, meta.MinRamMb);
            var maxRamMb = Math.Max(minRamMb, meta.MaxRamMb);
            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add($"-Xms{minRamMb}M");
            psi.ArgumentList.Add($"-Xmx{maxRamMb}M");

            // Performance Improvements: Modern GC for Forge/Heavy servers
            psi.ArgumentList.Add("-XX:+UseG1GC");
            psi.ArgumentList.Add("-XX:+ParallelRefProcEnabled");
            psi.ArgumentList.Add("-XX:MaxGCPauseMillis=200");
            psi.ArgumentList.Add("-XX:+UnlockExperimentalVMOptions");
            psi.ArgumentList.Add("-XX:+DisableExplicitGC");
            psi.ArgumentList.Add("-XX:+AlwaysPreTouch");

            // Performance Improvements: Modern GC for Forge/Heavy servers

            foreach (var argument in TokenizeAdvancedJvmArgs(meta.AdvancedJvmArgs))
            {
                psi.ArgumentList.Add(argument);
            }

            // Architecture: Improved launch logic for modern Forge (1.17+)
            if (meta.ServerType == "Forge" && !File.Exists(serverJar))
            {
                // Modern Forge uses bootstrapper logic
                var winArgs = Directory.GetFiles(workingDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (winArgs != null)
                {
                    // Relative path from workingDir
                    string relativeArgs = Path.GetRelativePath(workingDir, winArgs);
                    psi.ArgumentList.Add($"@{relativeArgs}");
                }
                else
                {
                    // Fallback to server.jar if it was somehow generated or old Forge (1.16.5-)
                    if (File.Exists(serverJar)) {
                        psi.ArgumentList.Add("-jar");
                        psi.ArgumentList.Add("server.jar");
                    }
                }
            }
            else
            {
                psi.ArgumentList.Add("-jar");
                psi.ArgumentList.Add("server.jar");
            }
            
            psi.ArgumentList.Add("nogui");

            SetState(ServerState.Starting);
            _intentionalStop = false;

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;
            _process.Start();

            // Attach to job object so it dies with us
            try { _jobObject.AddProcess(_process.Handle); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assign Java process to the job object for instance {InstanceId}.", InstanceId);
            }

            // Start background readers
            _ = Task.Run(() => ReadStreamAsync(_process.StandardOutput, false));
            _ = Task.Run(() => ReadStreamAsync(_process.StandardError, true));
        }

        public async Task WriteInputAsync(string command)
        {
            if (_process != null && !_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync(command);
            }
        }

        public async Task StopAsync(int timeoutMs = 15000)
        {
            if (_process == null || _process.HasExited) return;

            _intentionalStop = true;
            SetState(ServerState.Stopping);

            // Send graceful /stop command
            await WriteInputAsync("stop");

            // Wait for exit
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await _process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Force kill if it doesn't stop in time
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to force-kill server instance {InstanceId} after stop timeout.", InstanceId);
                }
            }

            SetState(ServerState.Stopped);
        }

        public void Kill()
        {
            if (_process != null && !_process.HasExited)
            {
                _intentionalStop = true;
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill server instance {InstanceId}.", InstanceId);
                }

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

                    // Check output waiters
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
                _logger.LogDebug("Console stream reader for instance {InstanceId} stopped because the process was disposed.", InstanceId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "Console stream reader for instance {InstanceId} stopped because the process changed state.", InstanceId);
            }
        }

        private void AppendOutput(string line, bool isError = false)
        {
            string sanitizedLine = LogSanitizer.SanitizeConsoleLine(line);

            OutputBuffer.Enqueue(sanitizedLine);
            if (OutputBuffer.Count > MAX_BUFFER_LINES)
                OutputBuffer.TryDequeue(out _);

            try
            {
                _sessionLogWriter?.WriteLine(sanitizedLine);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to append output to the session log for instance {InstanceId}.", InstanceId);
            }

            if (isError)
                OnErrorLine?.Invoke(sanitizedLine);
            else
            {
                OnOutputLine?.Invoke(sanitizedLine);

                // State Transition
                if (State == ServerState.Starting && sanitizedLine.Contains("Done ("))
                    SetState(ServerState.Online);
                    
                // Player Tracking
                if (sanitizedLine.Contains(" joined the game"))
                    PlayerCount++;
                else if (sanitizedLine.Contains(" left the game"))
                {
                    PlayerCount--;
                    if (PlayerCount < 0) PlayerCount = 0;
                }
                else if (sanitizedLine.Contains("players online:"))
                {
                    var match = PlayerCountRegex.Match(sanitizedLine);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                    {
                        PlayerCount = count;
                    }
                }
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
                OnServerCrashed?.Invoke(CrashContext);
            }
            else
            {
                SetState(ServerState.Stopped);
            }

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

        /// <summary>
        /// Waits for a specific regex pattern to appear in the console output.
        /// Returns true if matched within timeout, false if timed out.
        /// Used by BackupService to synchronize with 'Saved the game'.
        /// </summary>
        public async Task<bool> WaitForConsoleOutputAsync(Regex regex, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _outputWaiters.TryAdd(tcs, regex);

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() =>
            {
                _outputWaiters.TryRemove(tcs, out _);
                tcs.TrySetResult(false); // Timed out
            });

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

        private static IEnumerable<string> TokenizeAdvancedJvmArgs(string? advancedJvmArgs)
        {
            if (string.IsNullOrWhiteSpace(advancedJvmArgs))
            {
                yield break;
            }

            foreach (Match match in AdvancedJvmArgTokenRegex.Matches(advancedJvmArgs))
            {
                var token = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (token.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                {
                    throw new InvalidOperationException("Advanced JVM arguments cannot contain control characters.");
                }

                if (token.Length >= 2 && token.StartsWith('"') && token.EndsWith('"'))
                {
                    token = token[1..^1];
                }

                yield return token;
            }
        }
    }
}
