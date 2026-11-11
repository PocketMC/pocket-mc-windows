using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Storage;

namespace PocketMC.Infrastructure.Instances;

public class ForgeInstaller
{
    private readonly VanillaProvider _vanillaProvider;
    private readonly DownloadMirrorService? _mirrorService;

    public ForgeInstaller(VanillaProvider vanillaProvider, DownloadMirrorService? mirrorService = null)
    {
        _vanillaProvider = vanillaProvider;
        _mirrorService = mirrorService;
    }

    public async Task HandleInstallerBasedSetupAsync(
        InstanceMetadata meta,
        string workingDir,
        string javaPath,
        Action<string> onLog,
        Action<ServerState>? onStateChange = null,
        CancellationToken cancellationToken = default)
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

            // The installer is a separate Java process that ignores our HTTP pipeline, so redirect
            // its embedded library URLs to the configured maven mirror before launching it.
            string runnableInstaller = "installer.jar";
            string? patchedInstallerPath = null;
            if (_mirrorService != null && _mirrorService.MirrorsEnabled && _mirrorService.MavenMirrorBase is { } mavenBase)
            {
                string candidatePath = Path.Combine(workingDir, "installer.mirrored.jar");
                if (InstallerMirrorPatcher.TryCreatePatchedCopy(installerPath, candidatePath, mavenBase))
                {
                    patchedInstallerPath = candidatePath;
                    runnableInstaller = Path.GetFileName(candidatePath);
                    onLog($"[PocketMC] Installer downloads redirected to {mavenBase}.");
                }
                else
                {
                    onLog("[PocketMC] WARNING: Could not prepare mirror-patched installer. Using the original installer.");
                }
            }

            var installerPsi = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = workingDir,
                Arguments = $"-Djava.awt.headless=true -Dforge.stdout=true -jar {runnableInstaller} --installServer",
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
                    var outputTask = Task.Run(() =>
                    {
                        int lineCount = 0;
                        int downloadCount = 0;
                        long lastReportTicks = Stopwatch.GetTimestamp();

                        onLog?.Invoke($"[PocketMC] {meta.ServerType} installer is running. This may take several minutes...");

                        while (!proc.StandardOutput.EndOfStream)
                        {
                            var line = proc.StandardOutput.ReadLine();
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            lineCount++;

                            bool isError = line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                                || line.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
                                || line.Contains("Exception", StringComparison.OrdinalIgnoreCase);

                            if (isError)
                            {
                                onLog?.Invoke($"[Installer Error] {line}");
                                continue;
                            }

                            if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Unpacking", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadCount++;
                            }

                            var elapsed = Stopwatch.GetElapsedTime(lastReportTicks);
                            if (elapsed.TotalMilliseconds >= 250)
                            {
                                onLog?.Invoke($"[Installer] {line.Trim()}");
                                lastReportTicks = Stopwatch.GetTimestamp();
                            }
                        }
                        onLog?.Invoke($"[PocketMC] Installer output stream completed ({lineCount} lines processed).");
                    });

                    var errorTask = Task.Run(() =>
                    {
                        while (!proc.StandardError.EndOfStream)
                        {
                            var line = proc.StandardError.ReadLine();
                            if (line != null) onLog?.Invoke($"[Error] {line}");
                        }
                    });

                    try
                    {
                        await proc.WaitForExitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        onLog?.Invoke($"[PocketMC] Installer cancelled. Cleaning up...");
                        try { proc.Kill(true); } catch { }
                        throw;
                    }
                    await Task.WhenAll(outputTask, errorTask);

                    if (proc.ExitCode == 0)
                    {
                        onLog?.Invoke($"[PocketMC] {meta.ServerType} installation successful.");
                        // Clean up installer to prevent re-runs
                        try { File.Delete(installerPath); } catch { }
                        if (patchedInstallerPath != null) { try { File.Delete(patchedInstallerPath); } catch { } }
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
        string verDir = Path.Combine(workingDir, "versions");
        if (Directory.Exists(verDir))
        {
            try { Directory.Delete(verDir, true); } catch { }
        }

        // The patched copy is disposable; keep installer.jar so the user can retry.
        string patchedInstaller = Path.Combine(workingDir, "installer.mirrored.jar");
        try { if (File.Exists(patchedInstaller)) File.Delete(patchedInstaller); } catch { }
    }
}
