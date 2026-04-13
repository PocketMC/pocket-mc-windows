using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Instances
{
    /// <summary>
    /// Encapsulates the logic for configuring a Minecraft server process launch.
    /// Extracts complex PSI construction from ServerProcess.
    /// </summary>
    public class ServerLaunchConfigurator
    {
        private static readonly Regex AdvancedJvmArgTokenRegex = new(
            "\"[^\"]*\"|\\S+",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private readonly JavaProvisioningService _javaProvisioning;
        private readonly ILogger<ServerLaunchConfigurator> _logger;

        public ServerLaunchConfigurator(JavaProvisioningService javaProvisioning, ILogger<ServerLaunchConfigurator> logger)
        {
            _javaProvisioning = javaProvisioning;
            _logger = logger;
        }

        public async Task<ProcessStartInfo> ConfigureAsync(InstanceMetadata meta, string workingDir, string appRootPath, Action<string> onLog)
        {
            int requiredJavaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(meta.MinecraftVersion);
            string javaPath = await EnsureAndResolveJavaPathAsync(meta, requiredJavaVersion, appRootPath, onLog);

            if (string.IsNullOrWhiteSpace(workingDir))
            {
                throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
            }

            // Forge auto-installation
            await HandleForgeInstallationAsync(meta, workingDir, javaPath, onLog);

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

            AddRamArguments(psi, meta);
            AddPerformanceArguments(psi);
            AddAdvancedArguments(psi, meta.AdvancedJvmArgs);
            AddExecutableArguments(psi, meta, workingDir);

            psi.ArgumentList.Add("nogui");

            return psi;
        }

        private async Task<string> EnsureAndResolveJavaPathAsync(InstanceMetadata meta, int requiredVersion, string appRootPath, Action<string> onLog)
        {
            // Architecture: Ensure required Java runtime is present and healthy (Auto-Repair)
            bool expectsBundled = string.IsNullOrWhiteSpace(meta.CustomJavaPath) ||
                                  JavaRuntimeResolver.IsBundledJavaPath(meta.CustomJavaPath, requiredVersion, appRootPath);

            bool missingCustom = !string.IsNullOrWhiteSpace(meta.CustomJavaPath) && !File.Exists(meta.CustomJavaPath);

            if (expectsBundled || missingCustom)
            {
                if (!_javaProvisioning.IsJavaVersionPresent(requiredVersion))
                {
                    onLog($"[PocketMC] Required Java {requiredVersion} is missing or corrupt. Starting auto-repair...");
                    try
                    {
                        await _javaProvisioning.EnsureJavaAsync(requiredVersion);
                        onLog($"[PocketMC] Java {requiredVersion} repaired successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Java auto-repair failed for instance {InstanceName}.", meta.Name);
                        onLog($"[PocketMC] CRITICAL: Java auto-repair failed: {ex.Message}");
                        throw;
                    }
                }
            }

            string javaPath = JavaRuntimeResolver.ResolveJavaPath(meta, appRootPath);
            string? bundledJavaPath = JavaRuntimeResolver.GetBundledJavaPath(appRootPath, requiredVersion);

            if (javaPath == "java")
            {
                _logger.LogWarning("Bundled Java {Version} not found for {Name}. Falling back to system java.", requiredVersion, meta.Name);
            }
            else if (bundledJavaPath != null && string.Equals(javaPath, bundledJavaPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Using bundled Java {Version} for {Name} at {Path}.", requiredVersion, meta.Name, javaPath);
            }

            return javaPath;
        }

        private async Task HandleForgeInstallationAsync(InstanceMetadata meta, string workingDir, string javaPath, Action<string> onLog)
        {
            string forgeInstaller = Path.Combine(workingDir, "forge-installer.jar");
            if (meta.ServerType == "Forge" && File.Exists(forgeInstaller) && !Directory.Exists(Path.Combine(workingDir, "libraries")))
            {
                onLog("[PocketMC] First-time Forge setup detected. Running installer...");

                var installerPsi = new ProcessStartInfo
                {
                    FileName = javaPath,
                    WorkingDirectory = workingDir,
                    Arguments = "-jar forge-installer.jar --installServer",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(installerPsi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0) onLog("[PocketMC] Forge installation successful.");
                    else throw new Exception($"Forge installer failed with exit code {proc.ExitCode}");
                }
            }
        }

        private void AddRamArguments(ProcessStartInfo psi, InstanceMetadata meta)
        {
            var minRamMb = Math.Max(128, meta.MinRamMb);
            var maxRamMb = Math.Max(minRamMb, meta.MaxRamMb);
            psi.ArgumentList.Add($"-Xms{minRamMb}M");
            psi.ArgumentList.Add($"-Xmx{maxRamMb}M");
        }

        private void AddPerformanceArguments(ProcessStartInfo psi)
        {
            psi.ArgumentList.Add("-XX:+UseG1GC");
            psi.ArgumentList.Add("-XX:+ParallelRefProcEnabled");
            psi.ArgumentList.Add("-XX:MaxGCPauseMillis=200");
            psi.ArgumentList.Add("-XX:+UnlockExperimentalVMOptions");
            psi.ArgumentList.Add("-XX:+DisableExplicitGC");
            psi.ArgumentList.Add("-XX:+AlwaysPreTouch");
        }

        private void AddAdvancedArguments(ProcessStartInfo psi, string? advancedArgs)
        {
            foreach (var argument in TokenizeAdvancedJvmArgs(advancedArgs))
            {
                psi.ArgumentList.Add(argument);
            }
        }

        private void AddExecutableArguments(ProcessStartInfo psi, InstanceMetadata meta, string workingDir)
        {
            string serverJar = Path.Combine(workingDir, "server.jar");

            if (meta.ServerType == "Forge" && !File.Exists(serverJar))
            {
                var winArgs = Directory.GetFiles(workingDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (winArgs != null)
                {
                    string relativeArgs = Path.GetRelativePath(workingDir, winArgs);
                    psi.ArgumentList.Add($"@{relativeArgs}");
                    return;
                }
            }

            // Fallback for non-Forge or old Forge
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add("server.jar");
        }

        private static IEnumerable<string> TokenizeAdvancedJvmArgs(string? advancedJvmArgs)
        {
            if (string.IsNullOrWhiteSpace(advancedJvmArgs)) yield break;

            foreach (Match match in AdvancedJvmArgTokenRegex.Matches(advancedJvmArgs))
            {
                var token = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(token)) continue;

                if (token.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new InvalidOperationException("Advanced JVM arguments cannot contain control characters.");

                if (token.Length >= 2 && token.StartsWith('"') && token.EndsWith('"'))
                    token = token[1..^1];

                yield return token;
            }
        }
    }
}
