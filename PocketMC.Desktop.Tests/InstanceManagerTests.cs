using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceManagerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RenameInstance_Normal_ChangesFolderName()
    {
        var manager = CreateManager(out var registry, out var pathService);
        var metadata = manager.CreateInstance("Old Name", "Desc");
        string oldPath = registry.GetPath(metadata.Id)!;
        Assert.Contains("old-name", oldPath);

        string newPath = manager.RenameInstance(metadata.Id, "New Name", "New Desc");

        Assert.Contains("new-name", newPath);
        Assert.True(Directory.Exists(newPath));
        Assert.False(Directory.Exists(oldPath));
        Assert.Equal("New Name", registry.GetById(metadata.Id)!.Name);
        Assert.Equal(newPath, registry.GetPath(metadata.Id));
    }

    [Fact]
    public void RenameInstance_CaseOnly_WorksSafely()
    {
        var manager = CreateManager(out var registry, out var pathService);
        var metadata = manager.CreateInstance("Server", "Desc");
        string oldPath = registry.GetPath(metadata.Id)!;
        Assert.EndsWith("server", oldPath);

        // We want to "rename" the slug to "SERVER" (SlugHelper currently lowercases, so we'll test the logic itself)
        // Wait, SlugHelper.GenerateSlug(input) calls input.ToLowerInvariant().
        // So "Server" and "SERVER" both result in "server".
        // If the user wants to rename the DISPLAY NAME to uppercase, the folder slug remains the same.
        // But if we ever change SlugHelper to allow case, we need this logic.
        // Let's test renaming from "Server 1" to "Server 2" first.
        string newPath = manager.RenameInstance(metadata.Id, "SERVER", "Desc");

        // Since SlugHelper lowercases, newSlug == oldSlug == "server". 
        // The folder shouldn't move, but metadata should update.
        Assert.Equal(oldPath, newPath);
        Assert.Equal("SERVER", registry.GetById(metadata.Id)!.Name);
    }

    [Fact]
    public void RenameInstance_Conflict_ResolvesWithSuffix()
    {
        var manager = CreateManager(out var registry, out var pathService);
        var server1 = manager.CreateInstance("My Server", "First");
        var server2 = manager.CreateInstance("Other", "Second");

        // Rename server2 to "My Server"
        string newPath = manager.RenameInstance(server2.Id, "My Server", "Second Renamed");

        Assert.Contains("my-server-2", newPath);
        Assert.True(Directory.Exists(newPath));
        Assert.Equal("My Server", registry.GetById(server2.Id)!.Name);
    }

    [Fact]
    public void RenameInstance_InvalidCharacters_SanitizesSlug()
    {
        var manager = CreateManager(out var registry, out var pathService);
        var metadata = manager.CreateInstance("Safe Name", "Desc");

        string newPath = manager.RenameInstance(metadata.Id, "Unsafe!@# Name$%^", "Desc");

        Assert.Contains("unsafe-name", newPath);
        Assert.True(Directory.Exists(newPath));
    }

    private class MockAssetProvider : PocketMC.Desktop.Core.Interfaces.IAssetProvider
    {
        public Stream? GetAssetStream(string assetName) => null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private InstanceManager CreateManager(out InstanceRegistry registry, out InstancePathService pathService)
    {
        var state = new ApplicationState();
        state.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });

        pathService = new InstancePathService(state);
        registry = new InstanceRegistry(pathService, NullLogger<InstanceRegistry>.Instance);

        return new InstanceManager(registry, pathService, state, new MockAssetProvider(), NullLogger<InstanceManager>.Instance, new EmptyServiceProvider());
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



