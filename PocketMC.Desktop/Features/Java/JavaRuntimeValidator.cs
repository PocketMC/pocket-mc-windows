using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Java
{
    /// <summary>
    /// Utility for validating and verifying Java runtime installations.
    /// </summary>
    public sealed class JavaRuntimeValidator
    {
        private readonly ILogger<JavaRuntimeValidator> _logger;

        public JavaRuntimeValidator(ILogger<JavaRuntimeValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Checks if a Java installation folder contains a functional java.exe.
        /// </summary>
        public bool IsRuntimePresent(string versionPath)
        {
            string javaExe = Path.Combine(versionPath, "bin", "java.exe");
            return File.Exists(javaExe) && new FileInfo(javaExe).Length > 1024 * 10;
        }

        /// <summary>
        /// Performs an active verification by running 'java -version'.
        /// </summary>
        public async Task ValidateRuntimeAsync(string runtimePath, CancellationToken cancellationToken)
        {
            string javaExePath = Path.Combine(runtimePath, "bin", "java.exe");
            if (!File.Exists(javaExePath))
            {
                throw new FileNotFoundException("java.exe was not found in the provided runtime path.", javaExePath);
            }

            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaExePath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start java.exe for validation.");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                string stdErr = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Java validation failed (Exit Code: {process.ExitCode}): {stdErr}");
            }
        }
    }
}
