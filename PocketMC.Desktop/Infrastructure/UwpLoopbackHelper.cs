using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace PocketMC.Desktop.Infrastructure;

/// <summary>
/// Manages the Windows UWP network loopback exemption required for Minecraft
/// Bedrock Edition (a UWP app) to connect to a locally-hosted Bedrock Dedicated
/// Server (BDS).  By default, UWP apps are sandboxed and cannot initiate outbound
/// connections to 127.0.0.1 or ::1.  This class grants the exemption once via the
/// built-in <c>CheckNetIsolation.exe</c> tool using an elevated sub-process.
/// </summary>
public static class UwpLoopbackHelper
{
    private static readonly TimeSpan CheckNetIsolationTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The Windows Store package family name for Minecraft Bedrock Edition (UWP).
    /// </summary>
    private const string MinecraftPackageFamilyName = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";

    /// <summary>
    /// Returns <c>true</c> when the Minecraft UWP package already appears in the
    /// loopback-exempt list (i.e. the fix has been applied previously).
    /// </summary>
    public static bool IsExemptionPresent()
    {
        try
        {
            return IsExemptionPresentAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Adds the Minecraft UWP package to the loopback-exempt list by launching
    /// <c>CheckNetIsolation.exe</c> with a UAC elevation prompt.  The method is
    /// fire-and-forget; it awaits the process exit but does not throw on failure
    /// — the caller should surface any errors through the UI.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the exemption was applied successfully; <c>false</c> if the
    /// user cancelled the UAC prompt or if the tool reported an error.
    /// </returns>
    public static async Task<bool> ApplyExemptionAsync()
    {
        try
        {
            // "runas" forces the UAC elevation dialog so CheckNetIsolation can
            // write to the protected loopback-exemption registry hive.
            var psi = new DiagnosticsProcessStartInfo
            {
                FileName = "CheckNetIsolation.exe",
                Arguments = $"LoopbackExempt -a -n=\"{MinecraftPackageFamilyName}\"",
                UseShellExecute = true,  // required for "runas" verb
                Verb = "runas",
                CreateNoWindow = false   // shell must be visible for UAC dialog
            };

            using var proc = DiagnosticsProcess.Start(psi);
            if (proc == null) return false;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined the UAC prompt; not an error per se.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsExemptionPresentAsync()
    {
        var psi = new DiagnosticsProcessStartInfo
        {
            FileName = "CheckNetIsolation.exe",
            Arguments = "LoopbackExempt -s",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = DiagnosticsProcess.Start(psi);
        if (proc == null) return false;

        using var cts = new CancellationTokenSource(CheckNetIsolationTimeout);
        Task<string> outputTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        Task<string> errorTask = proc.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(proc);
            return false;
        }

        return proc.ExitCode == 0 &&
               outputTask.Result.Contains(MinecraftPackageFamilyName, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKillProcessTree(DiagnosticsProcess proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort timeout cleanup; callers already receive false.
        }
    }
}
