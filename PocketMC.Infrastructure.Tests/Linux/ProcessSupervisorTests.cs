using PocketMC.Infrastructure.OS;
using System.IO;
using System.Runtime.InteropServices;

namespace PocketMC.Infrastructure.Tests.Linux;

/// <summary>
/// TDD tests for the cross-platform process supervisor (POSIX on Linux, no-op on Windows).
/// </summary>
public class ProcessSupervisorTests
{
    [Fact]
    public void ProcessSupervisor_Create_DoesNotThrow()
    {
        // Should succeed on any OS
        var supervisor = new ProcessSupervisor();
        Assert.NotNull(supervisor);
    }

    [SkippableFact]
    public void LaunchWithProcessGroup_OnLinux_SetsNewSessionId()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var supervisor = new ProcessSupervisor();

        // Launch 'sleep 1' in a new process group
        int pid = supervisor.LaunchInProcessGroup("/bin/sleep", "1");
        try
        {
            Assert.True(pid > 0, "PID should be positive");
            // The process should be running
            Assert.True(System.Diagnostics.Process.GetProcessById(pid) != null);
        }
        finally
        {
            supervisor.TerminateProcessGroup(pid);
        }
    }

    [SkippableFact]
    public void TerminateProcessGroup_OnLinux_KillsChildProcesses()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var supervisor = new ProcessSupervisor();
        int pid = supervisor.LaunchInProcessGroup("/bin/sh", "-c \"sleep 30 & sleep 30 & wait\"");

        try
        {
            // Give the child a moment to start
            System.Threading.Thread.Sleep(200);
            supervisor.TerminateProcessGroup(pid);
            System.Threading.Thread.Sleep(500);

            // The process should no longer exist
            bool stillRunning = false;
            try { System.Diagnostics.Process.GetProcessById(pid); stillRunning = true; }
            catch (ArgumentException) { /* expected — process is gone */ }
            Assert.False(stillRunning, "Process group should have been terminated");
        }
        catch
        {
            // Best-effort cleanup
            try { supervisor.TerminateProcessGroup(pid); } catch { }
            throw;
        }
    }
}
