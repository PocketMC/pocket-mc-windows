using PocketMC.Application.Services.Instances;

namespace PocketMC.Application.Tests.Services.Instances;

public sealed class DataPackLibraryTests : IDisposable
{
    private readonly string _world = Path.Combine(Path.GetTempPath(), "pmc-dp-" + Guid.NewGuid().ToString("N"));

    public DataPackLibraryTests()
    {
        Directory.CreateDirectory(Path.Combine(_world, "datapacks", "folderpack"));
        File.WriteAllText(Path.Combine(_world, "datapacks", "folderpack", "pack.mcmeta"), "{}");
        File.WriteAllText(Path.Combine(_world, "datapacks", "a.zip"), "x");
        File.WriteAllText(Path.Combine(_world, "datapacks", "notes.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_world, true);

    [Fact]
    public void List_ReturnsOnlyPacks()
    {
        var names = DataPackLibrary.List(_world).Select(p => p.Name).ToList();
        Assert.Equal(new[] { "a.zip", "folderpack" }, names);
    }

    [Fact]
    public void Disable_ThenEnable_MovesPackBetweenFolders()
    {
        DataPackLibrary.SetEnabled(_world, "a.zip", false);
        Assert.False(File.Exists(Path.Combine(_world, "datapacks", "a.zip")));
        Assert.False(DataPackLibrary.List(_world).Single(p => p.Name == "a.zip").IsEnabled);

        DataPackLibrary.SetEnabled(_world, "a.zip", true);
        Assert.True(DataPackLibrary.List(_world).Single(p => p.Name == "a.zip").IsEnabled);
    }

    [Fact]
    public void Disable_WhenDestinationExists_ThrowsAndKeepsBothPacks()
    {
        Directory.CreateDirectory(Path.Combine(_world, "datapacks.disabled"));
        File.WriteAllText(Path.Combine(_world, "datapacks.disabled", "a.zip"), "other");

        Assert.Throws<IOException>(() => DataPackLibrary.SetEnabled(_world, "a.zip", false));

        Assert.Equal("x", File.ReadAllText(Path.Combine(_world, "datapacks", "a.zip")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(_world, "datapacks.disabled", "a.zip")));
    }

    [Fact]
    public void Disable_Folder_MovesWholeFolder()
    {
        DataPackLibrary.SetEnabled(_world, "folderpack", false);
        Assert.True(File.Exists(Path.Combine(_world, "datapacks.disabled", "folderpack", "pack.mcmeta")));
    }

    [Fact]
    public void Remove_DeletesPack_AndRejectsTraversal()
    {
        DataPackLibrary.Remove(_world, "a.zip");
        Assert.DoesNotContain(DataPackLibrary.List(_world), p => p.Name == "a.zip");
        Assert.Throws<InvalidDataException>(() => DataPackLibrary.Remove(_world, "../x.zip"));
    }
}
