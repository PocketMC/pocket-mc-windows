using PocketMC.Application.Services.Instances;
using PocketMC.Domain.Models;
using PocketMC.Domain.Storage;

namespace PocketMC.Application.Tests.Services.Safety;

[Collection("Maintenance regressions")]
public sealed class RecoveryRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC-Recovery-" + Guid.NewGuid().ToString("N"));
    public RecoveryRegressionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string Write(string relative, string text)
    {
        string path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
    }

    [Fact]
    public void Properties_RoundTripHashesEscapesAndLogicalLines()
    {
        string path = Write("server.properties", "password=hello #literal\nmotd=first\\\n  second\nunicode=\\u263a\n");
        var values = ServerPropertiesParser.Read(path);
        Assert.Equal("hello #literal", values["password"]);
        Assert.Equal("firstsecond", values["motd"]);
        Assert.Equal("☺", values["unicode"]);
        values["motd"] = "welcome\nenable-rcon=true";
        ServerPropertiesParser.Write(path, values);
        Assert.DoesNotContain("\nenable-rcon=true", File.ReadAllText(path));
        Assert.Equal(values, ServerPropertiesParser.Read(path));
    }

    [Fact]
    public void Inventory_IncludesCustomWorldAndSiblingDimensions()
    {
        Write("server.properties", "level-name=custom\n");
        Write("custom/level.dat", "world"); Write("custom_nether/level.dat", "nether"); Write("custom_the_end/level.dat", "end");
        var worlds = WorldInventory.Resolve(_root, new InstanceMetadata { ServerType = "Paper" });
        Assert.Equal(new[] { "custom", "custom_nether", "custom_the_end" }, worlds.Select(w => w.RelativePath));
    }

    [Fact]
    public void FailedMultiRootCommit_RestoresEarlierFiles()
    {
        string original = Write("world/level.dat", "original");
        string stage = Write("stage/level.dat", "replacement");
        Assert.Throws<FileNotFoundException>(() => InstanceFileTransaction.Commit(_root, new[] {
            new InstanceFileTransaction.Replacement(Path.GetDirectoryName(stage)!, "world"),
            new InstanceFileTransaction.Replacement(Path.Combine(_root, "missing"), "world_nether") }));
        Assert.Equal("original", File.ReadAllText(original));
        Assert.False(File.Exists(Path.Combine(_root, ".pocketmc-file-transaction.json")));
    }

    [Fact]
    public void InterruptedTransaction_IsRolledBackOnRecovery()
    {
        Write("world/level.dat", "replacement");
        Write(".pocketmc-rollback-repro/0/level.dat", "original");
        var journal = new InstanceFileTransaction.Journal(new() { new("world", ".pocketmc-rollback-repro/0", true) });
        Write(".pocketmc-file-transaction.json", System.Text.Json.JsonSerializer.Serialize(journal));
        InstanceFileTransaction.Recover(_root);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_root, "world/level.dat")));
    }

    [Fact]
    public async Task WaitingForOneInstance_DoesNotBlockItsRuntimeOrAnotherInstance()
    {
        var coordinator = new InstanceOperationCoordinator();
        using var owner = await coordinator.AcquireAsync(_root);
        Task<IDisposable> queued = coordinator.AcquireAsync(_root);
        using (await coordinator.AcquireAsync(Path.Combine(_root, "runtime")).WaitAsync(TimeSpan.FromSeconds(2))) { }
        owner.Dispose();
        using var next = await queued.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Coordinator_BlocksMutationOfRunningWorld_AndDrainsMaintenance()
    {
        var coordinator = new InstanceOperationCoordinator();
        coordinator.SetRunning(_root, true);
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync(_root, true)); }
        finally { coordinator.SetRunning(_root, false); }
        using (await coordinator.BeginMaintenanceAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync(_root));
        using var allowed = await coordinator.AcquireAsync(_root, true);
    }
}

[CollectionDefinition("Maintenance regressions", DisableParallelization = true)]
public sealed class MaintenanceRegressionCollection { }
