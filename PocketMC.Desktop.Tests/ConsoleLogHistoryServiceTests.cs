using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Console;

namespace PocketMC.Desktop.Tests;

public sealed class ConsoleLogHistoryServiceTests
{
    [Fact]
    public void PrepareNewSessionLog_RotatesCurrentLogToLastLog()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();
        string currentPath = Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName);
        File.WriteAllLines(currentPath, new[] { "old line 1", "old line 2" });

        string newCurrentPath = service.PrepareNewSessionLog(workspace.InstancePath, new DateTime(2026, 5, 25, 10, 20, 30, DateTimeKind.Utc));

        Assert.Equal(currentPath, newCurrentPath);
        Assert.True(File.Exists(currentPath));
        Assert.Equal(string.Empty, File.ReadAllText(currentPath));
        Assert.Equal(new[] { "old line 1", "old line 2" }, File.ReadAllLines(Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.LastSessionLogName)));
    }

    [Fact]
    public void PrepareNewSessionLog_ArchivesTimestampedSessionLog()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();
        File.WriteAllText(Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName), "archived");

        service.PrepareNewSessionLog(workspace.InstancePath, new DateTime(2026, 5, 25, 10, 20, 30, DateTimeKind.Utc));

        string archivedPath = Path.Combine(workspace.LogsPath, "sessions", "pocketmc-session-20260525-102030.log");
        Assert.True(File.Exists(archivedPath));
        Assert.Equal("archived", File.ReadAllText(archivedPath));
    }

    [Fact]
    public async Task LoadSessionTailAsync_LoadsCurrentSessionWhenNoProcessExists()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();
        File.WriteAllLines(Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName), new[] { "last run line" });

        ConsoleLogReadResult result = await service.LoadSessionTailAsync(workspace.InstancePath, maxLines: 100, preferCurrentSession: true);

        Assert.Equal(new[] { "last run line" }, result.Lines);
        Assert.Equal(ConsoleSessionLogKind.CurrentSession, result.Kind);
        Assert.False(result.IsLive);
    }

    [Fact]
    public async Task LoadSessionTailAsync_FallsBackToLegacyPocketMcSessionLog()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();
        File.WriteAllLines(Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.LegacySessionLogName), new[] { "legacy line" });

        ConsoleLogReadResult result = await service.LoadSessionTailAsync(workspace.InstancePath, maxLines: 100, preferCurrentSession: true);

        Assert.Equal(new[] { "legacy line" }, result.Lines);
        Assert.Equal(ConsoleSessionLogKind.LegacySession, result.Kind);
    }

    [Fact]
    public async Task LoadSessionTailAsync_DoesNotLoadMoreThanBufferSizeLines()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();
        File.WriteAllLines(
            Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName),
            Enumerable.Range(1, 20).Select(i => $"line {i}"));

        ConsoleLogReadResult result = await service.LoadSessionTailAsync(workspace.InstancePath, maxLines: 5, preferCurrentSession: true);

        Assert.Equal(5, result.Lines.Count);
        Assert.Equal("line 16", result.Lines[0]);
        Assert.Equal("line 20", result.Lines[^1]);
    }

    private static ConsoleLogHistoryService CreateService()
        => new(NullLogger<ConsoleLogHistoryService>.Instance);

    private sealed class ConsoleLogHistoryWorkspace : IDisposable
    {
        public ConsoleLogHistoryWorkspace()
        {
            InstancePath = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));
            LogsPath = Path.Combine(InstancePath, "logs");
            Directory.CreateDirectory(LogsPath);
        }

        public string InstancePath { get; }

        public string LogsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(InstancePath))
            {
                Directory.Delete(InstancePath, recursive: true);
            }
        }
    }
}


