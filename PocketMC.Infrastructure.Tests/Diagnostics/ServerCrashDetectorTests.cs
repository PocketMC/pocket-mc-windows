using System;
using System.Collections.Generic;
using System.IO;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances.Diagnostics;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Diagnostics;

public class ServerCrashDetectorTests : IDisposable
{
    private readonly string _testDir;

    public ServerCrashDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pocketmc_crash_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Analyze_IntentionalStop_ReturnsCleanExit()
    {
        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Fabric",
            StateBeforeExit: ServerState.Stopping,
            IntentionalStop: true,
            ExitCode: 0,
            ProcessStartTime: DateTime.UtcNow.AddMinutes(-5),
            OutputBufferLines: new[] { "Stopping server", "Saving worlds", "Thread stopped" });

        var result = ServerCrashDetector.Analyze(context);

        Assert.False(result.IsCrash);
        Assert.Equal(CrashCategory.None, result.Category);
    }

    [Fact]
    public void Analyze_FabricMissingDependency_DetectsModDependencyCrash_EvenIfExitCodeIsZero()
    {
        var logs = new List<string>
        {
            "[main/INFO]: Loading Minecraft 1.20.1 with Fabric Loader 0.15.11",
            "[main/WARN]: Incompatible mod set found!",
            "[main/ERROR]: net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!",
            "\t- Mod 'create' (create) 0.5.1-f requires version 0.90.0 or later of 'fabric-api', but only 0.85.0 is present!",
            "\t- Mod 'farmersdelight' requires 'fabric-api'",
            "[main/INFO]: Stopping server"
        };

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Fabric",
            StateBeforeExit: ServerState.Starting,
            IntentionalStop: false,
            ExitCode: 0, // Fabric exits with 0 on handled dependency failure!
            ProcessStartTime: DateTime.UtcNow.AddSeconds(-10),
            OutputBufferLines: logs);

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.ModDependency, result.Category);
        Assert.Contains("fabric-api", result.Summary);
        Assert.True(result.IsFatalModConfigurationError);
    }

    [Fact]
    public void Analyze_ForgeModLoadingException_DetectsMissingMod_EvenIfExitCodeIsZero()
    {
        var logs = new List<string>
        {
            "[main/INFO]: Forge Mod Loader version 47.2.0 for Minecraft 1.20.1 loading",
            "[main/FATAL]: [fml.earlydisplay.EarlyLoadingException]: Missing or incompatible mods found:",
            "Mod 'jei' requires 'forge' 47.1.3 or above",
            "[main/INFO]: Shutting down"
        };

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Forge",
            StateBeforeExit: ServerState.Starting,
            IntentionalStop: false,
            ExitCode: 0,
            ProcessStartTime: DateTime.UtcNow.AddSeconds(-10),
            OutputBufferLines: logs);

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.MissingMod, result.Category);
        Assert.True(result.IsFatalModConfigurationError);
    }

    [Fact]
    public void Analyze_MixinTransformerError_DetectsMixinConflict()
    {
        var logs = new List<string>
        {
            "[main/INFO]: Injecting Mixins...",
            "[main/ERROR]: org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError: An unexpected critical error was encountered",
            "Critical injection failure: Cannot apply mixin custom.mixins.json to net.minecraft.server.MinecraftServer",
            "[main/INFO]: Process finished with exit code 1"
        };

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Fabric",
            StateBeforeExit: ServerState.Starting,
            IntentionalStop: false,
            ExitCode: 1,
            ProcessStartTime: DateTime.UtcNow.AddSeconds(-10),
            OutputBufferLines: logs);

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.MixinConflict, result.Category);
        Assert.True(result.IsFatalModConfigurationError);
    }

    [Fact]
    public void Analyze_DiskCrashReport_DetectsDiskReportAuthoritatively()
    {
        string crashReportsDir = Path.Combine(_testDir, "crash-reports");
        Directory.CreateDirectory(crashReportsDir);

        string reportPath = Path.Combine(crashReportsDir, "crash-2026-08-18_22.00.00-server.txt");
        File.WriteAllText(reportPath, @"---- Minecraft Crash Report ----
// Don't do that.

Time: 2026-08-18 22:00:00
Description: Ticking entity in world 'world'

java.lang.NullPointerException: Cannot invoke method on null entity
	at net.minecraft.world.entity.LivingEntity.tick(LivingEntity.java:123)
");

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Paper",
            StateBeforeExit: ServerState.Online,
            IntentionalStop: false,
            ExitCode: 0,
            ProcessStartTime: DateTime.UtcNow.AddMinutes(-1),
            OutputBufferLines: new[] { "Saving players", "Closing world", "Server stopped" });

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.ServerTickException, result.Category);
        Assert.Equal(reportPath, result.CrashReportPath);
        Assert.Contains("Ticking entity", result.Summary);
    }

    [Fact]
    public void Analyze_OutOfMemory_DetectsOom()
    {
        var logs = new List<string>
        {
            "[Server thread/ERROR]: Encountered an unexpected exception",
            "java.lang.OutOfMemoryError: Java heap space",
            "\tat java.base/java.util.Arrays.copyOf(Arrays.java:3537)"
        };

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Paper",
            StateBeforeExit: ServerState.Online,
            IntentionalStop: false,
            ExitCode: 137,
            ProcessStartTime: DateTime.UtcNow.AddMinutes(-10),
            OutputBufferLines: logs);

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.OutOfMemory, result.Category);
        Assert.Contains("Java heap space", result.Summary);
    }

    [Fact]
    public void Analyze_PrematureExitDuringStarting_DetectsStartupAborted()
    {
        var logs = new List<string>
        {
            "Loading libraries, please wait...",
            "Process exited prematurely."
        };

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Vanilla",
            StateBeforeExit: ServerState.Starting,
            IntentionalStop: false,
            ExitCode: 0,
            ProcessStartTime: DateTime.UtcNow.AddSeconds(-5),
            OutputBufferLines: logs);

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.StartupAborted, result.Category);
    }

    [Fact]
    public void Analyze_BedrockScriptError_DetectsBedrockScript()
    {
        var logs = new List<string>
        {
            "[INFO] Starting Server",
            "[ERROR] Script execution error in system.js line 42: ReferenceError: player is not defined",
            "Quit correctly"
        };

        var context = new ServerCrashContext(
            WorkingDirectory: _testDir,
            ServerType: "Bedrock",
            StateBeforeExit: ServerState.Online,
            IntentionalStop: false,
            ExitCode: 0,
            ProcessStartTime: DateTime.UtcNow.AddMinutes(-2),
            OutputBufferLines: logs);

        var result = ServerCrashDetector.Analyze(context);

        Assert.True(result.IsCrash);
        Assert.Equal(CrashCategory.BedrockScript, result.Category);
    }
}
