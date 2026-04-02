using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

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
        private Process? _process;
        private readonly JobObject _jobObject;
        private bool _disposed;
        private bool _intentionalStop;
        private readonly ConcurrentDictionary<TaskCompletionSource<bool>, Regex> _outputWaiters = new();

        public Guid InstanceId { get; }
        public ServerState State { get; private set; } = ServerState.Stopped;
        public ConcurrentQueue<string> OutputBuffer { get; } = new();

        public int PlayerCount { get; private set; }

        public event Action<string>? OnOutputLine;
        public event Action<string>? OnErrorLine;
        public event Action<int>? OnExited;
        public event Action<ServerState>? OnStateChanged;
        public event Action<string>? OnServerCrashed;

        public Process? GetInternalProcess() => _process;

        public ServerProcess(Guid instanceId, JobObject jobObject)
        {
            InstanceId = instanceId;
            _jobObject = jobObject;
        }

        public void Start(InstanceMetadata meta, string appRootPath)
        {
            if (State != ServerState.Stopped && State != ServerState.Crashed)
                throw new InvalidOperationException($"Cannot start server — current state is {State}.");

            string jreFolder = "java21";
            if (System.Version.TryParse(meta.MinecraftVersion.Replace("1.X", "1.0").Split('-')[0], out var versionParts))
            {
                if (versionParts.Minor <= 17) jreFolder = "java11";
                else if (versionParts.Minor <= 20 && versionParts.Build <= 4) jreFolder = "java17";
            }
            if (meta.MinecraftVersion.StartsWith("1.20.4") || meta.MinecraftVersion.StartsWith("1.20.3")) jreFolder = "java17";

            string javaPath = Path.Combine(appRootPath, "runtime", jreFolder, "bin", "java.exe");
            if (!File.Exists(javaPath))
            {
                throw new FileNotFoundException(
                    $"Java runtime not found for {meta.MinecraftVersion}.\n\n" +
                    $"Expected path: {javaPath}\n\n");
            }

            string serversDir = Path.Combine(appRootPath, "servers");
            string? workingDir = null;
            if (Directory.Exists(serversDir))
            {
                foreach (var dir in Directory.GetDirectories(serversDir))
                {
                    string metaFile = Path.Combine(dir, ".pocket-mc.json");
                    if (File.Exists(metaFile) && File.ReadAllText(metaFile).Contains(meta.Id.ToString()))
                    {
                        workingDir = dir;
                        break;
                    }
                }
            }

            if (workingDir == null)
            {
                throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
            }

            string serverJar = Path.Combine(workingDir, "server.jar");
            if (!File.Exists(serverJar))
            {
                throw new FileNotFoundException(
                    $"server.jar not found in:\n{workingDir}\n\n" +
                    $"Please download a Minecraft server JAR and place it there.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-Xms{meta.MinRamMb}M -Xmx{meta.MaxRamMb}M -jar server.jar nogui",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            SetState(ServerState.Starting);
            _intentionalStop = false;

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;
            _process.Start();

            // Attach to job object so it dies with us
            try { _jobObject.AddProcess(_process.Handle); }
            catch { /* Job object may fail on some configs — process still managed */ }

            // Start background readers
            Task.Run(() => ReadStreamAsync(_process.StandardOutput, false));
            Task.Run(() => ReadStreamAsync(_process.StandardError, true));
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
                try { _process.Kill(entireProcessTree: true); } catch { }
            }

            SetState(ServerState.Stopped);
        }

        public void Kill()
        {
            if (_process != null && !_process.HasExited)
            {
                _intentionalStop = true;
                try { _process.Kill(entireProcessTree: true); } catch { }
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
                    OutputBuffer.Enqueue(line);
                    if (isError)
                        OnErrorLine?.Invoke(line);
                    else
                    {
                        OnOutputLine?.Invoke(line);

                        // State Transition
                        if (State == ServerState.Starting && line.Contains("Done ("))
                            SetState(ServerState.Online);
                            
                        // Player Tracking
                        if (line.Contains(" joined the game"))
                            PlayerCount++;
                        else if (line.Contains(" left the game"))
                        {
                            PlayerCount--;
                            if (PlayerCount < 0) PlayerCount = 0;
                        }
                        else if (line.Contains("players online:"))
                        {
                            var match = Regex.Match(line, @"There are (\d+) of a max");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                            {
                                PlayerCount = count;
                            }
                        }

                        // Check output waiters (used by BackupService for save-all sync)
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
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            int exitCode = _process?.ExitCode ?? -1;

            if (!_intentionalStop && exitCode != 0)
            {
                var snapshotLines = OutputBuffer.ToArray().TakeLast(50);
                string crashContext = $"--- CRASH DETECTED (Exit Code: {exitCode}) ---\n" + string.Join(Environment.NewLine, snapshotLines);
                
                SetState(ServerState.Crashed);
                OnServerCrashed?.Invoke(crashContext);
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
        public async Task<bool> WaitForConsoleOutputAsync(string regexPattern, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            var compiled = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _outputWaiters.TryAdd(tcs, compiled);

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
                Kill();
                _process?.Dispose();
            }
        }
    }
}
