using System.Text.Json;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class SummaryStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC_SummaryTests_" + Guid.NewGuid());
    private readonly SummaryStorageService _service = new();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp files.
        }
    }

    [Fact]
    public void Read_ReturnsNull_WhenFileNameEscapesSummaryDirectory()
    {
        string serverDir = Path.Combine(_root, "server");
        Directory.CreateDirectory(serverDir);

        string outsidePath = Path.Combine(_root, "outside-summary.json");
        File.WriteAllText(outsidePath, JsonSerializer.Serialize(CreateSummary("outside")));

        SessionSummary? summary = _service.Read(serverDir, Path.Combine("..", "..", "outside-summary.json"));

        Assert.Null(summary);
    }

    [Fact]
    public void Delete_ReturnsFalseAndDoesNotDelete_WhenFileNameEscapesSummaryDirectory()
    {
        string serverDir = Path.Combine(_root, "server");
        Directory.CreateDirectory(serverDir);

        string outsidePath = Path.Combine(_root, "outside-summary.json");
        File.WriteAllText(outsidePath, JsonSerializer.Serialize(CreateSummary("outside")));

        bool deleted = _service.Delete(serverDir, Path.Combine("..", "..", "outside-summary.json"));

        Assert.False(deleted);
        Assert.True(File.Exists(outsidePath));
    }

    [Fact]
    public void Save_StillPersistsAndListsContainedSummary()
    {
        string serverDir = Path.Combine(_root, "server");

        string path = _service.Save(serverDir, CreateSummary("contained"));

        Assert.True(File.Exists(path));
        SessionSummary summary = Assert.Single(_service.ListSummaries(serverDir));
        Assert.Equal("contained", summary.Content);
    }

    private static SessionSummary CreateSummary(string content)
    {
        return new SessionSummary
        {
            ServerName = "Test",
            SessionStart = DateTime.UtcNow.AddMinutes(-5),
            SessionEnd = DateTime.UtcNow,
            Duration = TimeSpan.FromMinutes(5),
            Content = content,
            AiProvider = "Test"
        };
    }
}
