using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances.Diagnostics;

/// <summary>
/// Execution context used for evaluating server crashes.
/// </summary>
public record ServerCrashContext(
    string WorkingDirectory,
    string ServerType,
    ServerState StateBeforeExit,
    bool IntentionalStop,
    int ExitCode,
    DateTime? ProcessStartTime,
    IReadOnlyList<string> OutputBufferLines);

/// <summary>
/// Multi-layered crash detector that analyzes disk artifacts, startup lifecycle state,
/// modloader signatures, and exit codes to reliably diagnose Minecraft server crashes.
/// </summary>
public static class ServerCrashDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    public static CrashAnalysisResult Analyze(ServerCrashContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 1. Intentional user stop is NEVER a crash
        if (context.IntentionalStop)
        {
            return CrashAnalysisResult.CleanExit(context.ExitCode);
        }

        var lines = context.OutputBufferLines ?? Array.Empty<string>();

        // 2. Layer 1: Disk Crash Reports (Most Authoritative Proof)
        var diskReport = CheckDiskCrashReports(context.WorkingDirectory, context.ProcessStartTime);
        if (diskReport != null)
        {
            diskReport.ExitCode = context.ExitCode;
            return diskReport;
        }

        // 3. Layer 2: ModLoader & Engine Error Pattern Scanner (Heuristic Log Analysis)
        var logCrash = AnalyzeLogSignatures(lines, context.ExitCode);
        if (logCrash != null)
        {
            return logCrash;
        }

        // 4. Layer 3: Premature Startup Exit (Terminated before reaching Online)
        if (context.StateBeforeExit is ServerState.Starting or ServerState.SettingUp or ServerState.Installing)
        {
            return BuildStartupFailureResult(lines, context.ExitCode);
        }

        // 5. Layer 4: Non-Zero OS Exit Code
        if (context.ExitCode != 0)
        {
            return BuildExitCodeFailureResult(lines, context.ExitCode);
        }

        // Normal clean shutdown
        return CrashAnalysisResult.CleanExit(context.ExitCode);
    }

    // ── Layer 1: Disk Reports ────────────────────────────────────────────────

    private static CrashAnalysisResult? CheckDiskCrashReports(string workingDir, DateTime? processStartTime)
    {
        if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
            return null;

        try
        {
            // 1. Java / Forge / Fabric crash-reports/ directory
            string crashReportsDir = Path.Combine(workingDir, "crash-reports");
            if (Directory.Exists(crashReportsDir))
            {
                var minTime = (processStartTime ?? DateTime.UtcNow.AddMinutes(-5)).AddSeconds(-15);
                var latestReport = Directory.GetFiles(crashReportsDir, "crash-*.txt")
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.LastWriteTimeUtc >= minTime)
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestReport != null)
                {
                    return ParseJavaCrashReport(latestReport.FullName);
                }
            }

            // 2. PocketMine-MP crashdumps/ directory
            string crashdumpsDir = Path.Combine(workingDir, "crashdumps");
            if (Directory.Exists(crashdumpsDir))
            {
                var minTime = (processStartTime ?? DateTime.UtcNow.AddMinutes(-5)).AddSeconds(-15);
                var latestDump = Directory.GetFiles(crashdumpsDir, "*.*")
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.LastWriteTimeUtc >= minTime)
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestDump != null)
                {
                    return ParsePocketMineCrashDump(latestDump.FullName);
                }
            }
        }
        catch
        {
            // Disk scan failure should not prevent subsequent layers
        }

        return null;
    }

    private static CrashAnalysisResult ParseJavaCrashReport(string filePath)
    {
        string title = "Minecraft Server Crash Report";
        string summary = "The server generated a crash report on disk.";
        var fullText = "";

        try
        {
            fullText = File.ReadAllText(filePath, Encoding.UTF8);
            var lines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            string? description = null;
            var stacktraceSnippet = new List<string>();
            bool capturingStack = false;

            foreach (var line in lines.Take(120))
            {
                if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                {
                    description = line.Substring("Description:".Length).Trim();
                }
                else if (line.Contains("java.lang.") || line.Contains("Caused by:") || line.Contains("at net.minecraft"))
                {
                    capturingStack = true;
                }

                if (capturingStack && stacktraceSnippet.Count < 25)
                {
                    stacktraceSnippet.Add(line);
                }
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                summary = $"Crash: {description}";
            }
            else if (stacktraceSnippet.Count > 0)
            {
                summary = stacktraceSnippet[0].Trim();
            }
        }
        catch
        {
            fullText = $"Crash report generated at: {filePath}";
        }

        return new CrashAnalysisResult
        {
            IsCrash = true,
            Category = CrashCategory.ServerTickException,
            Title = title,
            Summary = summary,
            FullLogContext = fullText.Length > 15000 ? fullText[..15000] : fullText,
            CrashReportPath = filePath
        };
    }

    private static CrashAnalysisResult ParsePocketMineCrashDump(string filePath)
    {
        string fullText = "";
        string summary = "PocketMine-MP fatal crash dump generated.";

        try
        {
            fullText = File.ReadAllText(filePath, Encoding.UTF8);
            var firstLine = fullText.Split('\n').FirstOrDefault(l => l.Contains("Error:") || l.Contains("Exception:"));
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                summary = firstLine.Trim();
            }
        }
        catch
        {
            fullText = $"Crash dump generated at: {filePath}";
        }

        return new CrashAnalysisResult
        {
            IsCrash = true,
            Category = CrashCategory.PocketMineFatal,
            Title = "PocketMine-MP Crash Dump",
            Summary = summary,
            FullLogContext = fullText,
            CrashReportPath = filePath
        };
    }

    // ── Layer 2: Log Signatures ──────────────────────────────────────────────

    private static CrashAnalysisResult? AnalyzeLogSignatures(IReadOnlyList<string> lines, int exitCode)
    {
        if (lines.Count == 0) return null;

        // Scan all lines for specific fatal patterns
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];

            // 1. Fabric: Incompatible Mod Set / Missing Dependencies
            if (line.Contains("Incompatible mod set found!", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("net.fabricmc.loader.impl.FormattedException", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("net.fabricmc.loader.impl.discovery.ModResolutionException", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 40);
                string summary = ExtractFabricMissingModsSummary(lines, i) 
                    ?? "Fabric failed to load because one or more mod dependencies are missing or incompatible.";

                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.ModDependency,
                    Title = "Fabric Mod Dependency Error",
                    Summary = summary,
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }

            // 2. Forge / NeoForge: Missing or incompatible mods
            if (line.Contains("Missing or incompatible mods found:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("net.minecraftforge.fml.ModLoadingException", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("fml.earlydisplay.EarlyLoadingException", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ModLoadingErrors", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 40);
                string summary = ExtractForgeMissingModsSummary(lines, i)
                    ?? "Forge/NeoForge failed to load due to missing or incompatible mod dependencies.";

                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.MissingMod,
                    Title = "Forge Mod Loading Error",
                    Summary = summary,
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }

            // 3. Mixin Transformer & Injection Conflicts
            if (line.Contains("org.spongepowered.asm.mixin.transformer.throwables.", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("MixinTransformerError", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Critical injection failure:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("MixinApplyError", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 45);
                string summary = ExtractMixinSummary(lines, i)
                    ?? "A Mixin injection collision occurred between two or more installed mods.";

                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.MixinConflict,
                    Title = "Mixin Injection Conflict",
                    Summary = summary,
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }

            // 4. Out of Memory (OOM)
            if (line.Contains("java.lang.OutOfMemoryError", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("GC overhead limit exceeded", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 30);
                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.OutOfMemory,
                    Title = "Server Out of Memory (OOM)",
                    Summary = "Java heap space was exhausted. Increase RAM allocation in Server Settings.",
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }

            // 5. Bedrock BDS Script Execution Error
            if (line.Contains("[ERROR] Script execution error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Segmentation fault", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 35);
                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.BedrockScript,
                    Title = "Bedrock Script / Engine Error",
                    Summary = "A Bedrock script or addon module caused an engine execution failure.",
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }

            // 6. PocketMine Fatal Error
            if (line.Contains("Fatal error:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Uncaught Error:", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 35);
                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.PocketMineFatal,
                    Title = "PocketMine-MP Fatal Error",
                    Summary = line.Trim(),
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }
        }

        // Check for generic fatal Java exceptions in the last 100 lines
        int startScan = Math.Max(0, lines.Count - 100);
        for (int i = startScan; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Contains("Exception in thread \"main\"", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Exception in thread \"Server thread\"", StringComparison.OrdinalIgnoreCase))
            {
                var block = ExtractLogBlock(lines, i, 50);
                return new CrashAnalysisResult
                {
                    IsCrash = true,
                    Category = CrashCategory.JavaRuntime,
                    Title = "Java Runtime Exception",
                    Summary = line.Trim(),
                    FullLogContext = block,
                    ExitCode = exitCode
                };
            }
        }

        return null;
    }

    // ── Layer 3 & 4 Builders ─────────────────────────────────────────────────

    private static CrashAnalysisResult BuildStartupFailureResult(IReadOnlyList<string> lines, int exitCode)
    {
        var tail = lines.TakeLast(60).ToList();
        string summary = "The server stopped unexpectedly during startup before reaching the Online state.";

        // Attempt to find any error or exception in the startup log
        var errorLine = tail.LastOrDefault(l => 
            l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Exception", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(errorLine))
        {
            summary = errorLine.Trim();
        }

        return new CrashAnalysisResult
        {
            IsCrash = true,
            Category = CrashCategory.StartupAborted,
            Title = "Server Startup Failed",
            Summary = summary,
            FullLogContext = string.Join(Environment.NewLine, tail),
            ExitCode = exitCode
        };
    }

    private static CrashAnalysisResult BuildExitCodeFailureResult(IReadOnlyList<string> lines, int exitCode)
    {
        var tail = lines.TakeLast(60).ToList();
        string summary = $"Server process terminated with non-zero exit code {exitCode}.";

        var errorLine = tail.LastOrDefault(l => 
            l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Exception", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(errorLine))
        {
            summary = errorLine.Trim();
        }

        return new CrashAnalysisResult
        {
            IsCrash = true,
            Category = CrashCategory.ProcessExitError,
            Title = $"Server Terminated (Exit Code {exitCode})",
            Summary = summary,
            FullLogContext = string.Join(Environment.NewLine, tail),
            ExitCode = exitCode
        };
    }

    // ── Helper Extractors ────────────────────────────────────────────────────

    private static string ExtractLogBlock(IReadOnlyList<string> lines, int startIndex, int count)
    {
        int start = Math.Max(0, startIndex - 2);
        int take = Math.Min(count, lines.Count - start);
        return string.Join(Environment.NewLine, lines.Skip(start).Take(take));
    }

    private static string? ExtractFabricMissingModsSummary(IReadOnlyList<string> lines, int startIndex)
    {
        var bullets = new List<string>();
        for (int i = startIndex; i < Math.Min(lines.Count, startIndex + 30); i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("• ") || trimmed.StartsWith("\t- "))
            {
                bullets.Add(trimmed);
            }
            else if (trimmed.Contains("Requires:", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("Unmet dependency:", StringComparison.OrdinalIgnoreCase))
            {
                bullets.Add(trimmed);
            }
        }

        if (bullets.Count > 0)
        {
            return string.Join(" | ", bullets.Take(3));
        }

        return null;
    }

    private static string? ExtractForgeMissingModsSummary(IReadOnlyList<string> lines, int startIndex)
    {
        for (int i = startIndex; i < Math.Min(lines.Count, startIndex + 25); i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("Mod ") || line.Contains("requires") || line.Contains("missing"))
            {
                return line;
            }
        }
        return null;
    }

    private static string? ExtractMixinSummary(IReadOnlyList<string> lines, int startIndex)
    {
        for (int i = startIndex; i < Math.Min(lines.Count, startIndex + 20); i++)
        {
            string line = lines[i].Trim();
            if (line.Contains("Critical injection failure") || line.Contains("Cannot apply mixin"))
            {
                return line;
            }
        }
        return null;
    }
}
