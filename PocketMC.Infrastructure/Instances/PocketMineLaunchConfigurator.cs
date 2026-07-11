using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances;

public class PocketMineLaunchConfigurator
{
    private static readonly Regex AdvancedJvmArgTokenRegex = new(
        "\"[^\"]*\"|\\S+",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly PhpProvisioningService _phpProvisioning;
    private readonly ILogger<PocketMineLaunchConfigurator> _logger;

    public PocketMineLaunchConfigurator(
        PhpProvisioningService phpProvisioning,
        ILogger<PocketMineLaunchConfigurator> logger)
    {
        _phpProvisioning = phpProvisioning;
        _logger = logger;
    }

    public async Task<ProcessStartInfo> ConfigureAsync(
        InstanceMetadata meta,
        string workingDir,
        string appRootPath,
        Action<string> onLog)
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
                    lines[i] = "level-type=DEFAULT";
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

    private void AddAdvancedArguments(ProcessStartInfo psi, string? advancedArgs)
    {
        foreach (var argument in TokenizeAdvancedJvmArgs(advancedArgs))
        {
            psi.ArgumentList.Add(argument);
        }
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
