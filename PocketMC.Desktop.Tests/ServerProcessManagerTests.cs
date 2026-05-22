using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Models;
using System.Text.Json;

namespace PocketMC.Desktop.Tests;

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

        string sessionLogPath = Path.Combine(instancePath, "logs", "pocketmc-session.log");
        try
        {
            processManager.KillProcess(metadata.Id);
            internalProcess?.WaitForExit(5000);

            File.Delete(sessionLogPath);
            Assert.False(File.Exists(sessionLogPath));
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
            if (process != null)
            {
                var internalProc = process.GetInternalProcess();
                try { processManager.KillProcess(metadata.Id); } catch {}
                try { internalProc?.WaitForExit(5000); } catch {}
            }
            processManager.ReleaseInstance(metadata.Id);
        }
    }
}
