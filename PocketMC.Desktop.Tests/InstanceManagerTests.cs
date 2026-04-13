using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceManagerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeleteInstanceAsync_RemovesInstanceDirectoryAndCacheEntry()
    {
        var manager = CreateManager(out var registry, out _);
        var metadata = manager.CreateInstance("Test Server", "A temporary server");
        string instancePath = registry.GetPath(metadata.Id)!;
        string lockedFile = Path.Combine(instancePath, "read-only.txt");
        File.WriteAllText(lockedFile, "data");
        File.SetAttributes(lockedFile, File.GetAttributes(lockedFile) | FileAttributes.ReadOnly);

        bool deleted = await manager.DeleteInstanceAsync(metadata.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(instancePath));
        Assert.Null(registry.GetPath(metadata.Id));
    }

    private InstanceManager CreateManager(out InstanceRegistry registry, out InstancePathService pathService)
    {
        var state = new ApplicationState();
        state.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });

        pathService = new InstancePathService(state);
        registry = new InstanceRegistry(pathService, NullLogger<InstanceRegistry>.Instance);

        return new InstanceManager(registry, pathService, state, NullLogger<InstanceManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            foreach (var file in Directory.GetFiles(_tempDirectory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
