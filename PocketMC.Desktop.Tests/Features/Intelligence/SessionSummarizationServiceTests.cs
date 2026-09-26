using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Intelligence;

public sealed class SessionSummarizationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC_SummarizeTests_" + Guid.NewGuid());
    private readonly SummaryStorageService _storageService = new();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void IsSummarizing_InitialState_ReturnsFalse()
    {
        var mockFactory = new Mock<ILlmProviderFactory>();
        var logHistory = new ConsoleLogHistoryService(NullLogger<ConsoleLogHistoryService>.Instance);
        var service = new SessionSummarizationService(
            mockFactory.Object,
            _storageService,
            logHistory,
            NullLogger<SessionSummarizationService>.Instance);

        Assert.False(service.IsSummarizing(Path.Combine(_root, "test-server")));
    }

    [Fact]
    public void GetLatestSummary_ReturnsMostRecentSummary()
    {
        var serverDir = Path.Combine(_root, "test-server");
        Directory.CreateDirectory(serverDir);

        var olderSummary = new SessionSummary
        {
            ServerName = "test-server",
            SessionStart = DateTime.UtcNow.AddHours(-2),
            SessionEnd = DateTime.UtcNow.AddHours(-1),
            Content = "Older summary content"
        };
        _storageService.Save(serverDir, olderSummary);

        var newerSummary = new SessionSummary
        {
            ServerName = "test-server",
            SessionStart = DateTime.UtcNow.AddMinutes(-30),
            SessionEnd = DateTime.UtcNow,
            Content = "Newer summary content"
        };
        _storageService.Save(serverDir, newerSummary);

        var mockFactory = new Mock<ILlmProviderFactory>();
        var logHistory = new ConsoleLogHistoryService(NullLogger<ConsoleLogHistoryService>.Instance);
        var service = new SessionSummarizationService(
            mockFactory.Object,
            _storageService,
            logHistory,
            NullLogger<SessionSummarizationService>.Instance);

        var latest = service.GetLatestSummary(serverDir);

        Assert.NotNull(latest);
        Assert.Equal("Newer summary content", latest.Content);
    }

    [Fact]
    public void GetLatestSummary_WhenNoSummariesExist_ReturnsNull()
    {
        var serverDir = Path.Combine(_root, "empty-server");
        Directory.CreateDirectory(serverDir);

        var mockFactory = new Mock<ILlmProviderFactory>();
        var logHistory = new ConsoleLogHistoryService(NullLogger<ConsoleLogHistoryService>.Instance);
        var service = new SessionSummarizationService(
            mockFactory.Object,
            _storageService,
            logHistory,
            NullLogger<SessionSummarizationService>.Instance);

        var latest = service.GetLatestSummary(serverDir);

        Assert.Null(latest);
    }
}
