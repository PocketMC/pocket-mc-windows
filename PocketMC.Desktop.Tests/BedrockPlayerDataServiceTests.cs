using PocketMC.Application.Services.Players;

namespace PocketMC.Desktop.Tests;

public sealed class BedrockPlayerDataServiceTests : IDisposable
{
    private readonly string _appDataDirectory;
    private readonly string _serverRoot;

    public BedrockPlayerDataServiceTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "PocketMC.BedrockPlayerData.Tests", Guid.NewGuid().ToString("N"));
        _appDataDirectory = Path.Combine(root, "app");
        _serverRoot = Path.Combine(root, "server");
        Directory.CreateDirectory(_appDataDirectory);
        Directory.CreateDirectory(_serverRoot);
    }

    [Fact]
    public async Task UpsertPlayerAsync_PersistsXuidNameMapAndAllowsReverseLookup()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);

        await service.UpsertPlayerAsync("2535452924809484", "SahajItaliya");

        Assert.Equal("2535452924809484", await service.GetXuidAsync("sahajitaliya"));
        Assert.Equal("SahajItaliya", await service.GetNameAsync("2535452924809484"));
    }

    [Fact]
    public async Task ImportPlayerMapFromLogLinesAsync_BackfillsConnectionLinesFromExistingOutput()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);

        int imported = await service.ImportPlayerMapFromLogLinesAsync(new[]
        {
            "[2026-04-28 18:10:30:571 INFO] Player connected: SahajItaliya, xuid: 2535452924809484",
            "[2026-04-28 18:10:38:971 INFO] There are 1/10 players online:"
        });

        Assert.Equal(1, imported);
        Assert.Equal("2535452924809484", await service.GetXuidAsync("SahajItaliya"));
    }

    [Fact]
    public async Task GetOppedPlayerNamesAsync_MapsOperatorXuidsThroughSidecar()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);
        await service.UpsertPlayerAsync("2535452924809484", "SahajItaliya");
        await service.UpsertPlayerAsync("2535453759792258", "AnotherPlayer");
        await File.WriteAllTextAsync(
            Path.Combine(_serverRoot, "permissions.json"),
            """
            [
              { "permission": "operator", "xuid": "2535452924809484" },
              { "permission": "member", "xuid": "2535453759792258" },
              { "permission": "operator", "xuid": "9999999999999999" }
            ]
            """);

        HashSet<string> oppedPlayers = await service.GetOppedPlayerNamesAsync();

        Assert.Contains("sahajitaliya", oppedPlayers);
        Assert.DoesNotContain("AnotherPlayer", oppedPlayers);
    }

    [Fact]
    public async Task GetOperatorXuidsAsync_ReturnsOnlyOperatorEntries()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);
        await File.WriteAllTextAsync(
            Path.Combine(_serverRoot, "permissions.json"),
            """
            [
              { "permission": "operator", "xuid": "2535452924809484" },
              { "permission": "member", "xuid": "2535453759792258" }
            ]
            """);

        HashSet<string> operatorXuids = await service.GetOperatorXuidsAsync();

        Assert.Contains("2535452924809484", operatorXuids);
        Assert.DoesNotContain("2535453759792258", operatorXuids);
    }

    [Fact]
    public async Task GetGamemodeAsync_DefaultsToSurvivalAndSaveGamemodeAsyncPersists()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);

        Assert.Equal("survival", await service.GetGamemodeAsync("SahajItaliya"));

        await service.SaveGamemodeAsync("SahajItaliya", "creative");

        Assert.Equal("creative", await service.GetGamemodeAsync("sahajitaliya"));
    }

    [Fact]
    public async Task AddBanAsyncAndRemoveBanAsyncMaintainBedrockBanSidecar()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);
        var entry = new BedrockBanEntry
        {
            Name = "SahajItaliya",
            Xuid = "2535452924809484",
            BannedAt = DateTime.UtcNow,
            Reason = "griefing",
            BannedBy = "console"
        };

        await service.AddBanAsync(entry);
        List<BedrockBanEntry> bansAfterAdd = await service.GetBansAsync();

        Assert.Single(bansAfterAdd);
        Assert.Equal("SahajItaliya", bansAfterAdd[0].Name);

        await service.RemoveBanAsync("sahajitaliya");
        List<BedrockBanEntry> bansAfterRemove = await service.GetBansAsync();

        Assert.Empty(bansAfterRemove);
    }

    [Fact]
    public async Task GetBanForPlayerAsync_MatchesByNameOrXuid()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);
        await service.AddBanAsync(new BedrockBanEntry
        {
            Name = "OldName",
            Xuid = "2535452924809484",
            BannedAt = DateTime.UtcNow,
            Reason = "griefing",
            BannedBy = "console"
        });

        BedrockBanEntry? byName = await service.GetBanForPlayerAsync("oldname", null);
        BedrockBanEntry? byXuid = await service.GetBanForPlayerAsync("NewName", "2535452924809484");

        Assert.NotNull(byName);
        Assert.NotNull(byXuid);
        Assert.Equal("griefing", byXuid!.Reason);
    }

    [Fact]
    public async Task WatchPermissionsFile_RaisesDebouncedCallback()
    {
        var service = new BedrockPlayerDataService(_appDataDirectory, _serverRoot);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable watcher = service.WatchPermissionsFile(() => changed.TrySetResult());

        await File.WriteAllTextAsync(Path.Combine(_serverRoot, "permissions.json"), "[]");

        Task completed = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(changed.Task, completed);
    }

    public void Dispose()
    {
        string root = Directory.GetParent(_appDataDirectory)!.FullName;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}


