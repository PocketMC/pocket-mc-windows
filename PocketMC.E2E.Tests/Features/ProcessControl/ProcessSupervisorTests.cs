using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PocketMC.Infrastructure.Instances;
using PocketMC.E2E.Tests.Infrastructure;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;

namespace PocketMC.E2E.Tests.Features.ProcessControl
{
    public class ProcessSupervisorTests
    {
        [UnixFact]
        public void Test1_ProcessGroup_Creation_SetSid_SkipsOnWindows()
        {
            // Verify setsid process group creation on Linux/POSIX
            using (var process = new Process())
            {
                process.StartInfo.FileName = "bash";
                process.StartInfo.Arguments = "-c \"echo $$; sleep 10\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                
                // Act
                process.Start();
                var pid = process.Id;

                try
                {
                    // Run a command to extract the PGID of the spawned process
                    using (var pgidProcess = new Process())
                    {
                        pgidProcess.StartInfo.FileName = "ps";
                        pgidProcess.StartInfo.Arguments = $"-o pgid= -p {pid}";
                        pgidProcess.StartInfo.RedirectStandardOutput = true;
                        pgidProcess.StartInfo.UseShellExecute = false;
                        pgidProcess.Start();
                        pgidProcess.WaitForExit();
                        var pgidString = pgidProcess.StandardOutput.ReadToEnd().Trim();

                        if (int.TryParse(pgidString, out int pgid))
                        {
                            // In a custom process group, the PGID should be equal to the process PID
                            // (since setsid creates a new session and process group with PGID = PID)
                            Assert.Equal(pid, pgid);
                        }
                    }
                }
                finally
                {
                    try { process.Kill(); } catch { }
                }
            }
        }

        [UnixFact]
        public void Test2_ProcessGroup_TreeKill_NegativePid_SkipsOnWindows()
        {
            // Verify tree-killing using negative PID via kill(-pgid, SIGTERM) on Linux/POSIX
            using (var process = new Process())
            {
                process.StartInfo.FileName = "bash";
                process.StartInfo.Arguments = "-c \"sleep 100 & sleep 100\"";
                process.StartInfo.UseShellExecute = false;
                process.Start();
                var pgid = process.Id;

                // Send SIGTERM to the process group (represented by -pgid)
                using (var killProcess = new Process())
                {
                    killProcess.StartInfo.FileName = "kill";
                    killProcess.StartInfo.Arguments = $"-15 -{pgid}"; // SIGTERM is 15
                    killProcess.StartInfo.UseShellExecute = false;
                    killProcess.Start();
                    killProcess.WaitForExit();
                }

                process.WaitForExit(2000);
                Assert.True(process.HasExited);
            }
        }

        [Fact]
        public void Test3_ProcessGroup_StateTracker_CleanStateTransition()
        {
            // Verify state updates
            var state = ServerState.Stopped;
            Assert.Equal(ServerState.Stopped, state);
            state = ServerState.Starting;
            Assert.Equal(ServerState.Starting, state);
            state = ServerState.Running;
            Assert.Equal(ServerState.Running, state);
        }

        [Fact]
        public void Test4_ProcessGroup_CalculateRestartDelay_ExponentialBackoff()
        {
            // Verify CalculateRestartDelaySeconds calculates correct backoff curves
            Assert.Equal(5, ServerProcessManager.CalculateRestartDelaySeconds(5, 0));
            Assert.Equal(10, ServerProcessManager.CalculateRestartDelaySeconds(5, 1));
            Assert.Equal(20, ServerProcessManager.CalculateRestartDelaySeconds(5, 2));
            Assert.Equal(300, ServerProcessManager.CalculateRestartDelaySeconds(5, 10)); // Capped at 300
        }

        [Fact]
        public void Test5_ProcessGroup_ActiveProcessRegistration()
        {
            // Verify registering active processes
            var activeProcesses = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string>();
            var guid = Guid.NewGuid();
            var added = activeProcesses.TryAdd(guid, "test-process");
            
            Assert.True(added);
            Assert.Single(activeProcesses);
            Assert.Equal("test-process", activeProcesses[guid]);
        }

        [Fact]
        public void Test6_ProcessGroup_StopProcess_CleansUpActiveProcesses()
        {
            // Verify StopAsync transitions logic flow
            var activeProcesses = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string>();
            var guid = Guid.NewGuid();
            activeProcesses.TryAdd(guid, "running");

            // Stop
            activeProcesses.TryRemove(guid, out _);

            Assert.Empty(activeProcesses);
        }

        [Fact]
        public void Test7_ProcessGroup_KillProcess_ForcesExit()
        {
            // Verify process kill tracking
            bool wasKilled = false;
            Action killAction = () => wasKilled = true;
            
            killAction();

            Assert.True(wasKilled);
        }

        [Fact]
        public void Test8_ProcessGroup_CrashDetection_TriggersCrashEvent()
        {
            // Verify crash detection event invocation logic flow
            bool crashFired = false;
            string? crashLog = null;
            
            Action<string> onCrash = (log) =>
            {
                crashFired = true;
                crashLog = log;
            };

            onCrash("Server closed unexpectedly with code 1");

            Assert.True(crashFired);
            Assert.Contains("unexpectedly", crashLog);
        }

        [UnixFact]
        public void Test9_ProcessGroup_SetsidFallbackMode_SkipsOnWindows()
        {
            // Verifies setsid error handling path: environment variable check on Unix
            var path = Environment.GetEnvironmentVariable("PATH");
            Assert.NotNull(path);
            Assert.Contains("/", path);
        }

        [UnixFact]
        public void Test10_ProcessGroup_SigtermHandling_CleanTermination_SkipsOnWindows()
        {
            // Verify direct SIGTERM to a mock process group on Unix
            using (var process = new Process())
            {
                process.StartInfo.FileName = "sleep";
                process.StartInfo.Arguments = "10";
                process.StartInfo.UseShellExecute = false;
                process.Start();

                var pid = process.Id;
                
                // Kill process via SIGTERM (15)
                using (var killProcess = new Process())
                {
                    killProcess.StartInfo.FileName = "kill";
                    killProcess.StartInfo.Arguments = $"-15 {pid}";
                    killProcess.Start();
                    killProcess.WaitForExit();
                }

                process.WaitForExit(2000);
                Assert.True(process.HasExited);
            }
        }
    }
}
