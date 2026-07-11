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

    [Fact]
    public async Task LoadSessionTailAsync_WithLargeFile_CorrectlyExtractsTail()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();

        // Write a ~5MB file (well above the 64KB sequential threshold)
        const int totalLines = 50_000;
        const int requestedTail = 100;
        string logPath = Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName);

        using (var writer = new StreamWriter(logPath))
        {
            for (int i = 1; i <= totalLines; i++)
            {
                writer.WriteLine($"[10:00:{i % 60:D2}] [Server thread/INFO]: Line number {i} with some padding text to increase file size");
            }
        }

        // Sanity check: file must exceed 64KB
        Assert.True(new FileInfo(logPath).Length > 65536, "Test file should exceed 64KB to exercise the seek-backward path.");

        ConsoleLogReadResult result = await service.LoadSessionTailAsync(workspace.InstancePath, maxLines: requestedTail, preferCurrentSession: true);

        Assert.Equal(requestedTail, result.Lines.Count);

        // Verify the last line is the final line written
        Assert.Contains($"Line number {totalLines}", result.Lines[^1]);

        // Verify the first returned line is the correct offset from the end
        int expectedFirstLine = totalLines - requestedTail + 1;
        Assert.Contains($"Line number {expectedFirstLine}", result.Lines[0]);
    }

    [Fact]
    public async Task LoadSessionTailAsync_WithEmptyFile_ReturnsEmptyResult()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();

        // Write an empty file
        File.WriteAllText(Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName), "");

        ConsoleLogReadResult result = await service.LoadSessionTailAsync(workspace.InstancePath, maxLines: 10, preferCurrentSession: true);

        // Empty files have no content, so HasContent returns false and we get None
        Assert.Equal(ConsoleSessionLogKind.None, result.Kind);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task LoadSessionTailAsync_WithFewerLinesThanRequested_ReturnsAllLines()
    {
        using var workspace = new ConsoleLogHistoryWorkspace();
        var service = CreateService();
        File.WriteAllLines(
            Path.Combine(workspace.LogsPath, ConsoleLogHistoryService.CurrentSessionLogName),
            new[] { "alpha", "bravo", "charlie" });

        ConsoleLogReadResult result = await service.LoadSessionTailAsync(workspace.InstancePath, maxLines: 100, preferCurrentSession: true);

        Assert.Equal(3, result.Lines.Count);
        Assert.Equal("alpha", result.Lines[0]);
        Assert.Equal("charlie", result.Lines[^1]);
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


