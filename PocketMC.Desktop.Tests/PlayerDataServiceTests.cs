using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PocketMC.Application.Services.Players;

namespace PocketMC.Desktop.Tests;

public sealed class PlayerDataServiceTests : IDisposable
{
    private readonly string _serverRoot;

    public PlayerDataServiceTests()
    {
        _serverRoot = Path.Combine(Path.GetTempPath(), "PocketMC.PlayerData.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRoot);
    }

    [Fact]
    public async Task GetOppedPlayersAsync_ReadsOpsJsonCaseInsensitively()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_serverRoot, "ops.json"),
            """
            [
              { "uuid": "00000000-0000-0000-0000-000000000001", "name": "Sahaj33", "level": 4 },
              { "uuid": "00000000-0000-0000-0000-000000000002", "name": "BuilderOne", "level": 3 }
            ]
            """);

        var service = new PlayerDataService(_serverRoot);

        HashSet<string> oppedPlayers = await service.GetOppedPlayersAsync();

        Assert.Contains("sahaj33", oppedPlayers);
        Assert.Contains("BUILDERONE", oppedPlayers);
    }

    [Fact]
    public async Task GetUuidAsync_ReadsUsercacheByNameCaseInsensitively()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_serverRoot, "usercache.json"),
            """
            [
              { "name": "Sahaj33", "uuid": "00000000-0000-0000-0000-000000000123", "expiresOn": "2026-05-08 10:00:00 +0000" }
            ]
            """);

        var service = new PlayerDataService(_serverRoot);

        string? uuid = await service.GetUuidAsync("sahaj33");

        Assert.Equal("00000000-0000-0000-0000-000000000123", uuid);
    }

    [Fact]
    public async Task GetGamemodeAsync_ReturnsSurvivalWhenPlayerDataIsMissing()
    {
        var service = new PlayerDataService(_serverRoot);

        string gamemode = await service.GetGamemodeAsync("00000000-0000-0000-0000-000000000123");

        Assert.Equal("survival", gamemode);
    }

    [Fact]
    public async Task GetGamemodeAsync_ReadsPlayerGameTypeFromGzippedNbt()
    {
        string uuid = "00000000-0000-0000-0000-000000000123";
        string playerDataPath = Path.Combine(_serverRoot, "world", "playerdata", $"{uuid}.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(playerDataPath)!);
        WritePlayerDataFile(playerDataPath, playerGameType: 1);

        var service = new PlayerDataService(_serverRoot);

        string gamemode = await service.GetGamemodeAsync(uuid);

        Assert.Equal("creative", gamemode);
    }

    [Fact]
    public async Task WatchForChanges_RaisesOpsAndPlayerdataCallbacks()
    {
        string playerDataDirectory = Path.Combine(_serverRoot, "world", "playerdata");
        Directory.CreateDirectory(playerDataDirectory);

        var service = new PlayerDataService(_serverRoot);
        var opsChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var playerdataChanged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable watcher = service.WatchForChanges(
            () => opsChanged.TrySetResult(),
            uuid => playerdataChanged.TrySetResult(uuid));

        await File.WriteAllTextAsync(Path.Combine(_serverRoot, "ops.json"), "[]");
        string uuid = "00000000-0000-0000-0000-000000000123";
        await File.WriteAllTextAsync(Path.Combine(playerDataDirectory, $"{uuid}.dat"), "changed");

        await AssertCompletesAsync(opsChanged.Task);
        string changedUuid = await AssertCompletesAsync(playerdataChanged.Task);
        Assert.Equal(uuid, changedUuid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_serverRoot))
        {
            Directory.Delete(_serverRoot, recursive: true);
        }
    }

    private static async Task AssertCompletesAsync(Task task)
    {
        Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        await task;
    }

    private static async Task<T> AssertCompletesAsync<T>(Task<T> task)
    {
        Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        return await task;
    }

    private static void WritePlayerDataFile(string path, int playerGameType)
    {
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize);

        gzipStream.WriteByte(10); // TAG_Compound
        WriteBigEndianShort(gzipStream, 0);
        gzipStream.WriteByte(3); // TAG_Int
        WriteString(gzipStream, "playerGameType");
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(value, playerGameType);
        gzipStream.Write(value);
        gzipStream.WriteByte(0); // TAG_End
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteBigEndianShort(stream, (short)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteBigEndianShort(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}


