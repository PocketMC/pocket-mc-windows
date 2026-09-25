using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Tests.TestSupport.Fixtures;
using PocketMC.Domain.Models;
using System.Text.Json;

namespace PocketMC.Infrastructure.Tests.Instances;

public class ServerProcessManagerTests
{
    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 1, 20)]
    [InlineData(10, 2, 40)]
    [InlineData(10, 10, 300)]
    public void CalculateRestartDelaySeconds_UsesExponentialBackoffWithCap(int baseDelaySeconds, int attempts, int expectedDelay)
    {
        Assert.Equal(expectedDelay, ServerProcessManager.CalculateRestartDelaySeconds(baseDelaySeconds, attempts));
    }

    [Fact]
    public async Task KillProcess_ReleasesSessionLogHandle()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ServerProcessManager processManager = workspace.CreateServerProcessManager();
        InstanceMetadata metadata = workspace.CreateInstance("Bedrock Log Lock", serverType: "Bedrock (BDS)");
        string instancePath = workspace.GetInstancePath(metadata.Id);
        string serverExePath = Path.Combine(instancePath, "bedrock_server.exe");
        File.Copy(Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe", serverExePath);

        ServerProcess process = await processManager.StartProcessAsync(metadata, workspace.RootPath);
        System.Diagnostics.Process? internalProcess = process.GetInternalProcess();

        string sessionLogPath = Path.Combine(instancePath, "logs", LogConstants.CurrentSessionLogName);
        try
        {
            processManager.KillProcess(metadata.Id);
            internalProcess?.WaitForExit(5000);

            // Wait for the asynchronous exit handler to release the log handle
            bool deleted = false;
            for (int i = 0; i < 50; i++)
            {
                try
                {
                    File.Delete(sessionLogPath);
                    deleted = true;
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }
            Assert.True(deleted, "Log file was still locked after 5 seconds.");
        }
        finally
        {
            processManager.ReleaseInstance(metadata.Id);
        }
    }

    [Fact]
    public async Task StartProcessAsync_WhenLaunchSucceeds_PersistsLastPlayedAt()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ServerProcessManager processManager = workspace.CreateServerProcessManager();
        InstanceMetadata metadata = workspace.CreateInstance("Bedrock Last Played", serverType: "Bedrock (BDS)");
        string instancePath = workspace.GetInstancePath(metadata.Id);
        string serverExePath = Path.Combine(instancePath, "bedrock_server.exe");
        File.Copy(Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe", serverExePath);

        DateTime beforeStart = DateTime.UtcNow.AddSeconds(-1);
        ServerProcess? process = null;

        try
        {
            process = await processManager.StartProcessAsync(metadata, workspace.RootPath);

            string metadataJson = File.ReadAllText(workspace.PathService.GetMetadataPath(instancePath));
            InstanceMetadata savedMetadata = JsonSerializer.Deserialize<InstanceMetadata>(metadataJson)!;

            Assert.NotNull(savedMetadata.LastPlayedAt);
            Assert.True(savedMetadata.LastPlayedAt >= beforeStart);
            Assert.True(savedMetadata.LastPlayedAt <= DateTime.UtcNow.AddSeconds(1));
        }
        finally
        {
            try { processManager.KillProcess(metadata.Id); } catch { }
            processManager.ReleaseInstance(metadata.Id);
        }
    }

    [Fact]
    public async Task WriteInputAsync_ConcurrentCalls_ExecuteSafelyWithoutStreamCollision()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ServerProcessManager processManager = workspace.CreateServerProcessManager();
        InstanceMetadata metadata = workspace.CreateInstance("Bedrock Concurrent Stdin", serverType: "Bedrock (BDS)");
        string instancePath = workspace.GetInstancePath(metadata.Id);
        string serverExePath = Path.Combine(instancePath, "bedrock_server.exe");
        File.Copy(Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe", serverExePath);

        ServerProcess process = await processManager.StartProcessAsync(metadata, workspace.RootPath);

        try
        {
            var tasks = new System.Collections.Generic.List<Task>();
            for (int i = 0; i < 40; i++)
            {
                int index = i;
                if (index % 2 == 0)
                {
                    tasks.Add(Task.Run(() => process.WriteListCommandAsync()));
                }
                else
                {
                    tasks.Add(Task.Run(() => process.WriteInputAsync($"echo command {index}")));
                }
            }

            await Task.WhenAll(tasks);
        }
        finally
        {
            try { processManager.KillProcess(metadata.Id); } catch { }
            processManager.ReleaseInstance(metadata.Id);
        }
    }
}


