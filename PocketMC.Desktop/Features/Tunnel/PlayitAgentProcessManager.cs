using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Tunnel
{
    /// <summary>
    /// Handles the low-level lifecycle of the playit.exe process,
    /// including pipe redirection and job object association.
    /// </summary>
    public sealed class PlayitAgentProcessManager : IDisposable
    {
        private readonly JobObject _jobObject;
        private readonly ILogger<PlayitAgentProcessManager> _logger;
        private Process? _process;
        private StreamWriter? _logWriter;
        private bool _isDisposed;

        public event Action<string>? OnOutputLineReceived;
        public event Action<string>? OnErrorLineReceived;
        public event Action<int>? OnProcessExited;

        public PlayitAgentProcessManager(JobObject jobObject, ILogger<PlayitAgentProcessManager> logger)
        {
            _jobObject = jobObject;
            _logger = logger;
        }

        public bool IsRunning => _process != null && !_process.HasExited;
        public int? ProcessId => _process?.Id;

        public void Start(string executablePath, string logFilePath)
        {
            if (IsRunning) return;

            // Ensure log directory exists
            string? logDir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(logDir))
                Directory.CreateDirectory(logDir);

            _logWriter?.Dispose();
            _logWriter = new StreamWriter(logFilePath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--stdout",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += (s, e) => OnProcessExited?.Invoke(_process?.ExitCode ?? -1);

            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to start playit.exe process.");
            }

            try { _jobObject.AddProcess(_process.Handle); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to assign playit.exe to job object."); }

            Task.Run(() => ReadStreamAsync(_process.StandardOutput, OnOutputLineReceived));
            Task.Run(() => ReadStreamAsync(_process.StandardError, OnErrorLineReceived));
        }

        public void Stop()
        {
            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error killing playit process.");
            }
            finally
            {
                _process.Dispose();
                _process = null;
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }

        public void Log(string message)
        {
            string timestamped = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";
            try { _logWriter?.WriteLine(timestamped); }
            catch { /* Best effort */ }
        }

        private async Task ReadStreamAsync(StreamReader reader, Action<string>? onLine)
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    onLine?.Invoke(line);
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // Process ending
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Stop();
                _isDisposed = true;
            }
        }
    }
}
