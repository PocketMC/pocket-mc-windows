using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PocketMC.Infrastructure.OS;

/// <summary>
/// Cross-platform process supervisor.
/// On Linux, launches processes in a new POSIX process group (setsid equivalent)
/// and terminates entire groups via kill(-pgid, SIGTERM).
/// On Windows, this is a no-op wrapper — the Windows Job Object in JobObject.cs
/// handles lifetime management there.
/// </summary>
public sealed class ProcessSupervisor
{
    // POSIX signal numbers
    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int sig);

    /// <summary>
    /// Launches <paramref name="executable"/> with <paramref name="arguments"/> in a new
    /// process group on Linux (so kill(-pgid) can reach all descendants).
    /// Returns the PID of the launched process.
    /// On Windows, starts the process normally and returns its PID.
    /// </summary>
    public int LaunchInProcessGroup(string executable, string arguments)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsLinux())
        {
            // Setting a new process group is achieved by creating a new session.
            // .NET does not expose setsid() directly; the closest portable equivalent
            // is to prepend setsid as the launcher.
            psi = new ProcessStartInfo("/usr/bin/setsid", $"{executable} {arguments}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {executable}");

        return process.Id;
    }

    /// <summary>
    /// Sends SIGTERM to the entire process group identified by <paramref name="pid"/> on Linux.
    /// Falls back to SIGKILL after a short grace period if the process is still alive.
    /// On Windows, calls <see cref="Process.Kill(bool)"/> with entireProcessTree=true.
    /// </summary>
    public void TerminateProcessGroup(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            // Negative PID kills the entire process group
            PosixKill(-pid, SIGTERM);

            // Give a 500 ms grace period then SIGKILL
            System.Threading.Thread.Sleep(500);
            try { PosixKill(-pid, SIGKILL); } catch { /* already gone */ }
        }
        else
        {
            try
            {
                var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { /* process already exited */ }
        }
    }
}
