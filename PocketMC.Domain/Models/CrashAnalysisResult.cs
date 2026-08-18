using System;

namespace PocketMC.Domain.Models;

/// <summary>
/// Categorizes the underlying root cause of a Minecraft server crash.
/// </summary>
public enum CrashCategory
{
    None,
    ModDependency,
    MissingMod,
    MixinConflict,
    OutOfMemory,
    JavaRuntime,
    BedrockScript,
    PocketMineFatal,
    ServerTickException,
    StartupAborted,
    ProcessExitError,
    Unknown
}

/// <summary>
/// Holds structured diagnostic results from crash analysis.
/// </summary>
public class CrashAnalysisResult
{
    public bool IsCrash { get; set; }
    public CrashCategory Category { get; set; } = CrashCategory.None;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FullLogContext { get; set; } = string.Empty;
    public string? CrashReportPath { get; set; }
    public int ExitCode { get; set; }

    /// <summary>
    /// True if the crash is a deterministic mod dependency / configuration failure
    /// that cannot resolve itself through automatic retries.
    /// </summary>
    public bool IsFatalModConfigurationError =>
        Category is CrashCategory.ModDependency
                 or CrashCategory.MissingMod
                 or CrashCategory.MixinConflict;

    public static CrashAnalysisResult CleanExit(int exitCode = 0) => new()
    {
        IsCrash = false,
        Category = CrashCategory.None,
        ExitCode = exitCode
    };
}
