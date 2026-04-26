using PocketMC.Desktop.Features.Instances.Models;
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

namespace PocketMC.Desktop.Features.Instances.Services;
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
        private readonly PhpProvisioningService _phpProvisioning;
        private readonly VanillaProvider _vanillaProvider;
        private readonly ILogger<ServerLaunchConfigurator> _logger;

        public ServerLaunchConfigurator(
            JavaProvisioningService javaProvisioning, 
            PhpProvisioningService phpProvisioning,
            VanillaProvider vanillaProvider,
            ILogger<ServerLaunchConfigurator> logger)
        {
            _javaProvisioning = javaProvisioning;
            _phpProvisioning = phpProvisioning;
            _vanillaProvider = vanillaProvider;
            _logger = logger;
        }

        public async Task<ProcessStartInfo> ConfigureAsync(InstanceMetadata meta, string workingDir, string appRootPath, Action<string> onLog, Action<ServerState>? onStateChange = null)
        {
            if (string.IsNullOrWhiteSpace(workingDir))
            {
                throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
            }

            if (meta.ServerType != null && meta.ServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
            {
                return ConfigureBedrock(meta, workingDir, onLog);
            }

            if (meta.ServerType != null && meta.ServerType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
            {
                return await ConfigurePocketmineAsync(meta, workingDir, appRootPath, onLog);
            }

            // Java servers
            int requiredJavaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(meta.MinecraftVersion);
            string javaPath = await EnsureAndResolveJavaPathAsync(meta, requiredJavaVersion, appRootPath, onLog);

            // Forge/NeoForge auto-installation
            await HandleInstallerBasedSetupAsync(meta, workingDir, javaPath, onLog, onStateChange);

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
            AddPerformanceArguments(psi, meta.MinecraftVersion);
            AddAdvancedArguments(psi, meta.AdvancedJvmArgs);
            AddExecutableArguments(psi, meta, workingDir);

            psi.ArgumentList.Add("nogui");

            return psi;
        }

        private ProcessStartInfo ConfigureBedrock(InstanceMetadata meta, string workingDir, Action<string> onLog)
        {
            onLog("[PocketMC] Launching Bedrock Dedicated Server...");
            
            string executablePath = Path.Combine(workingDir, "bedrock_server.exe");
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"Bedrock server executable not found at {executablePath}. Ensure the ZIP was extracted correctly.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            return psi;
        }

        private async Task<ProcessStartInfo> ConfigurePocketmineAsync(InstanceMetadata meta, string workingDir, string appRootPath, Action<string> onLog)
        {
            onLog("[PocketMC] Verifying PHP runtime for Pocketmine-MP...");
            await _phpProvisioning.EnsurePhpAsync(null);

            string phpExePath = Path.Combine(appRootPath, "runtimes", "php", "bin", "php", "php.exe");
            if (!File.Exists(phpExePath))
            {
                throw new FileNotFoundException($"PHP executable not found at {phpExePath}.");
            }

            string pharPath = Path.Combine(workingDir, "PocketMine-MP.phar");
            if (!File.Exists(pharPath))
            {
                throw new FileNotFoundException($"PocketMine-MP.phar not found at {pharPath}.");
            }

            // ── PocketMine server.properties sanity fixes ────────────────────────
            // PocketMine only accepts: DEFAULT, FLAT, NETHER, THE_END, HELL
            // Java-style values like "minecraft:normal" or "default" (lowercase) cause:
            //   [ERROR]: Could not generate world: Unknown generator "minecraft:normal"
            PatchPocketmineServerProperties(workingDir, onLog);

            var psi = new ProcessStartInfo
            {
                FileName = phpExePath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add(pharPath);
            psi.ArgumentList.Add("--no-wizard");

            // Add pocketmine specific arguments, if any from advanced args
            AddAdvancedArguments(psi, meta.AdvancedJvmArgs);

            return psi;
        }

        /// <summary>
        /// Rewrites keys in server.properties that PocketMine-MP would otherwise reject.
        /// <list type="bullet">
        ///   <item><c>level-type</c>: Java namespaced values (e.g. <c>minecraft:normal</c>) → <c>DEFAULT</c></item>
        /// </list>
        /// </summary>
        private void PatchPocketmineServerProperties(string workingDir, Action<string> onLog)
        {
            string propsPath = Path.Combine(workingDir, "server.properties");
            if (!File.Exists(propsPath)) return;

            try
            {
                var lines = File.ReadAllLines(propsPath);
                bool changed = false;

                // Valid PocketMine generator names (case-sensitive in PM source).
                // Any other value for level-type causes an "Unknown generator" crash.
                var validPmGenerators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "DEFAULT", "FLAT", "NETHER", "THE_END", "HELL"
                };

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith("level-type", StringComparison.OrdinalIgnoreCase)) continue;

                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;

                    string currentValue = line[(eq + 1)..].Trim();
                    if (!validPmGenerators.Contains(currentValue))
                    {
                        lines[i] = $"level-type=DEFAULT";
                        onLog($"[PocketMC] Patched server.properties: level-type={currentValue} → DEFAULT (PocketMine does not support Java generator names)");
                        changed = true;
                    }
                }

                if (changed)
                    File.WriteAllLines(propsPath, lines);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not patch PocketMine server.properties; server may crash on first boot.");
            }
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

        private async Task HandleInstallerBasedSetupAsync(InstanceMetadata meta, string workingDir, string javaPath, Action<string> onLog, Action<ServerState>? onStateChange = null)
        {
            string installerPath = Path.Combine(workingDir, "installer.jar");
            bool isForgeOrNeo = meta.ServerType == "Forge" || meta.ServerType == "NeoForge";

            if (isForgeOrNeo && File.Exists(installerPath) && !Directory.Exists(Path.Combine(workingDir, "libraries")))
            {
                onStateChange?.Invoke(ServerState.Installing);
                onLog($"[PocketMC] First-time {meta.ServerType} setup detected. Running installer...");

                // Legacy Forge installers (pre-1.17) often fail to download the base Vanilla JAR 
                // themselves due to outdated URLs. We pre-download it to ensure success.
                if (meta.ServerType == "Forge" && JavaRuntimeResolver.TryParseVersion(meta.MinecraftVersion, out var version) && version < new Version(1, 17))
                {
                    string vanillaJarName = $"minecraft_server.{meta.MinecraftVersion}.jar";
                    string vanillaJarPath = Path.Combine(workingDir, vanillaJarName);
                    
                    if (!File.Exists(vanillaJarPath) && !File.Exists(Path.Combine(workingDir, "server.jar")))
                    {
                        onLog($"[PocketMC] Pre-downloading Vanilla {meta.MinecraftVersion} for legacy installer...");
                        try
                        {
                            await _vanillaProvider.DownloadSoftwareAsync(meta.MinecraftVersion!, vanillaJarPath);
                        }
                        catch (Exception ex)
                        {
                            onLog($"[PocketMC] WARNING: Base Vanilla download failed: {ex.Message}. Installer may fail.");
                        }
                    }
                }

                var installerPsi = new ProcessStartInfo
                {
                    FileName = javaPath,
                    WorkingDirectory = workingDir,
                    Arguments = "-Djava.awt.headless=true -Dforge.stdout=true -jar installer.jar --installServer",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                try
                {
                    using var proc = Process.Start(installerPsi);
                    if (proc != null)
                    {
                        // consume streams asynchronously to prevent deadlock
                        // Throttle output: Forge/NeoForge installers produce thousands of lines
                        // that can overwhelm the WPF UI. Only forward sampled/important lines.
                        var outputTask = Task.Run(() => {
                            int lineCount = 0;
                            long lastReportTicks = Stopwatch.GetTimestamp();
                            while (!proc.StandardOutput.EndOfStream)
                            {
                                var line = proc.StandardOutput.ReadLine();
                                if (line == null) continue;
                                lineCount++;

                                bool isImportant = line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                                    || line.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
                                    || line.Contains("Exception", StringComparison.OrdinalIgnoreCase);
                                var elapsed = Stopwatch.GetElapsedTime(lastReportTicks);

                                if (isImportant || lineCount % 50 == 0 || elapsed.TotalMilliseconds >= 500)
                                {
                                    onLog?.Invoke(line);
                                    lastReportTicks = Stopwatch.GetTimestamp();
                                }
                            }
                            onLog?.Invoke($"[PocketMC] Installer completed ({lineCount} lines processed).");
                        });

                        var errorTask = Task.Run(() => {
                            while (!proc.StandardError.EndOfStream)
                            {
                                var line = proc.StandardError.ReadLine();
                                if (line != null) onLog?.Invoke($"[Error] {line}");
                            }
                        });

                        await proc.WaitForExitAsync();
                        await Task.WhenAll(outputTask, errorTask);

                        if (proc.ExitCode == 0)
                        {
                            onLog?.Invoke($"[PocketMC] {meta.ServerType} installation successful.");
                            // Clean up installer to prevent re-runs
                            try { File.Delete(installerPath); } catch { }
                        }
                        else
                        {
                            // If installer failed, cleanup partial libraries to allow retry on next launch
                            CleanupFailedInstallation(workingDir);
                            throw new Exception($"{meta.ServerType} installer failed with exit code {proc.ExitCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    CleanupFailedInstallation(workingDir);
                    if (ex is not InvalidOperationException && !ex.Message.Contains("installer failed"))
                    {
                         throw new InvalidOperationException($"Failed to execute {meta.ServerType} installer: {ex.Message}", ex);
                    }
                    throw;
                }
            }
        }

        private void CleanupFailedInstallation(string workingDir)
        {
            string libDir = Path.Combine(workingDir, "libraries");
            if (Directory.Exists(libDir))
            {
                try { Directory.Delete(libDir, true); } catch { }
            }
        }

        private void AddRamArguments(ProcessStartInfo psi, InstanceMetadata meta)
        {
            var minRamMb = Math.Max(128, meta.MinRamMb);
            var maxRamMb = Math.Max(minRamMb, meta.MaxRamMb);
            psi.ArgumentList.Add($"-Xms{minRamMb}M");
            psi.ArgumentList.Add($"-Xmx{maxRamMb}M");
        }

        private void AddPerformanceArguments(ProcessStartInfo psi, string? mcVersion)
        {
            psi.ArgumentList.Add("-XX:+UseG1GC");
            psi.ArgumentList.Add("-XX:+ParallelRefProcEnabled");
            psi.ArgumentList.Add("-XX:MaxGCPauseMillis=200");
            psi.ArgumentList.Add("-XX:+UnlockExperimentalVMOptions");
            psi.ArgumentList.Add("-XX:+DisableExplicitGC");
            
            // Log4Shell Mitigation for vulnerable versions (1.8.8 - 1.18)
            if (JavaRuntimeResolver.TryParseVersion(mcVersion, out var version) && 
                version >= new Version(1, 8, 8) && 
                version < new Version(1, 18, 1))
            {
                psi.ArgumentList.Add("-Dlog4j2.formatMsgNoLookups=true");
            }

            // Performance: Only pre-touch for modern versions or if system has enough RAM normally.
            // On Windows with small pagefiles, this causes immediate crash for legacy users.
            if (version == null || version >= new Version(1, 17))
            {
                psi.ArgumentList.Add("-XX:+AlwaysPreTouch");
            }

            // Optimize for low-latency/throughput balance
            psi.ArgumentList.Add("-XX:G1NewSizePercent=30");
            psi.ArgumentList.Add("-XX:G1MaxNewSizePercent=40");
            psi.ArgumentList.Add("-XX:G1HeapRegionSize=8M");
            psi.ArgumentList.Add("-XX:G1ReservePercent=20");
            psi.ArgumentList.Add("-XX:G1HeapWastePercent=5");
            psi.ArgumentList.Add("-XX:G1MixedGCCountTarget=4");
            psi.ArgumentList.Add("-XX:InitiatingHeapOccupancyPercent=15");
            psi.ArgumentList.Add("-XX:G1MixedGCLiveThresholdPercent=90");
            psi.ArgumentList.Add("-XX:G1RSetUpdatingPauseTimePercent=5");
            psi.ArgumentList.Add("-XX:SurvivorRatio=32");
            psi.ArgumentList.Add("-XX:+PerfDisableSharedMem");
            psi.ArgumentList.Add("-XX:MaxTenuringThreshold=1");
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
            bool isForgeOrNeo = meta.ServerType == "Forge" || meta.ServerType == "NeoForge";
            string? chosenJar = null;

            if (isForgeOrNeo)
            {
                // Modern Forge/NeoForge (1.17+) use user_jvm_args.txt or win_args.txt
                var winArgs = Directory.GetFiles(workingDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (winArgs != null)
                {
                    string relativeArgs = Path.GetRelativePath(workingDir, winArgs);
                    psi.ArgumentList.Add($"@{relativeArgs}");
                    return;
                }

                // Legacy Forge (1.8.8 - 1.16.5)
                // Search for any forge jar that isn't the installer.
                // Prioritize 'universal' or 'server' but accept generic forge-* if it exists.
                var jars = Directory.GetFiles(workingDir, "*.jar")
                    .Select(Path.GetFileName)
                    .Where(f => f != null && f.Contains("forge", StringComparison.OrdinalIgnoreCase) && !f.Contains("installer", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f!.Contains("universal", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(f => f!.Contains("server", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                chosenJar = jars.FirstOrDefault();
            }

            // Fallback for Vanilla, Fabric, Paper, or if Forge jar detection failed
            if (string.IsNullOrEmpty(chosenJar))
            {
                chosenJar = "server.jar";
            }

            // Verification
            string fullJarPath = Path.Combine(workingDir, chosenJar);
            if (!File.Exists(fullJarPath))
            {
                throw new FileNotFoundException(
                    $"The server executable '{chosenJar}' was not found in the instance directory. " +
                    "If this is a Forge server, Ensure the installation completed successfully. " +
                    "You may need to 'Reinstall' the server software if it is missing.", chosenJar);
            }

            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(chosenJar);
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
