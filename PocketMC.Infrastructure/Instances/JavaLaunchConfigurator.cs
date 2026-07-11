using System;
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
using PocketMC.Infrastructure.Java;

namespace PocketMC.Infrastructure.Instances;

public class JavaLaunchConfigurator
{
    private static readonly Regex AdvancedJvmArgTokenRegex = new(
        "\"[^\"]*\"|\\S+",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly JavaProvisioningService _javaProvisioning;
    private readonly ForgeInstaller _forgeInstaller;
    private readonly ILogger<JavaLaunchConfigurator> _logger;

    public Func<int, string, Task<bool>> ConfirmJavaDownloadPrompt { get; set; } = (version, serverName) =>
    {
        return Task.FromResult(true);
    };

    public JavaLaunchConfigurator(
        JavaProvisioningService javaProvisioning,
        ForgeInstaller forgeInstaller,
        ILogger<JavaLaunchConfigurator> logger)
    {
        _javaProvisioning = javaProvisioning;
        _forgeInstaller = forgeInstaller;
        _logger = logger;
    }

    public async Task<ProcessStartInfo> ConfigureAsync(
        InstanceMetadata meta,
        string workingDir,
        string appRootPath,
        Action<string> onLog,
        Action<ServerState>? onStateChange = null,
        CancellationToken cancellationToken = default)
    {
        int requiredJavaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(meta);
        string javaPath = await EnsureAndResolveJavaPathAsync(meta, requiredJavaVersion, appRootPath, onLog);

        // Forge/NeoForge auto-installation
        await _forgeInstaller.HandleInstallerBasedSetupAsync(meta, workingDir, javaPath, onLog, onStateChange, cancellationToken);

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

    private async Task<string> EnsureAndResolveJavaPathAsync(InstanceMetadata meta, int requiredVersion, string appRootPath, Action<string> onLog)
    {
        bool expectsBundled = string.IsNullOrWhiteSpace(meta.CustomJavaPath) ||
                              JavaRuntimeResolver.IsBundledJavaPath(meta.CustomJavaPath, requiredVersion, appRootPath);

        bool missingCustom = !string.IsNullOrWhiteSpace(meta.CustomJavaPath) && !File.Exists(meta.CustomJavaPath);

        if (expectsBundled || missingCustom)
        {
            if (!_javaProvisioning.IsJavaVersionPresent(requiredVersion))
            {
                bool confirmed = await ConfirmJavaDownloadPrompt(requiredVersion, meta.Name);
                if (!confirmed)
                {
                    throw new InvalidOperationException($"Startup aborted: Java {requiredVersion} is required but was not downloaded.");
                }

                onLog($"[PocketMC] Required Java {requiredVersion} is missing. Starting download...");
                try
                {
                    await _javaProvisioning.EnsureJavaAsync(requiredVersion, isManualUserTriggered: true);
                    onLog($"[PocketMC] Java {requiredVersion} installed successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Java provisioning failed for instance {InstanceName}.", meta.Name);
                    onLog($"[PocketMC] CRITICAL: Java download failed: {ex.Message}");
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
            var winArgs = Directory.GetFiles(workingDir, "win_args.txt", SearchOption.AllDirectories).FirstOrDefault();
            if (winArgs != null)
            {
                string relativeArgs = Path.GetRelativePath(workingDir, winArgs);
                psi.ArgumentList.Add($"@{relativeArgs}");
                return;
            }

            var jars = Directory.GetFiles(workingDir, "*.jar")
                .Select(Path.GetFileName)
                .Where(f => f != null && f.Contains("forge", StringComparison.OrdinalIgnoreCase) && !f.Contains("installer", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f!.Contains("universal", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f!.Contains("server", StringComparison.OrdinalIgnoreCase))
                .ToList();

            chosenJar = jars.FirstOrDefault();
        }

        if (string.IsNullOrEmpty(chosenJar))
        {
            chosenJar = "server.jar";
        }

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
