using Xunit;
using Moq;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;

namespace PocketMC.Desktop.Tests;

public sealed class TelemetryServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.TelemetryTests", Guid.NewGuid().ToString("N"));

    public TelemetryServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Initialize_GeneratesTelemetryClientId_WhenMissing()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var settingsManager = new SettingsManager(settingsPath);

        var processManager = new ServerProcessManager(
            null!, null!, null!, null!, null!, null!, null!);
        var instanceRegistry = new InstanceRegistry(null!, null!);
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockLogger = new Mock<ILogger<TelemetryService>>();

        var telemetryService = new TelemetryService(
            settingsManager,
            processManager,
            instanceRegistry,
            mockHttpClientFactory.Object,
            mockLogger.Object);

        // Verify initial state
        var initialSettings = settingsManager.Load();
        Assert.Null(initialSettings.TelemetryClientId);

        telemetryService.Initialize();

        // Verify that ID is generated and saved
        var savedSettings = settingsManager.Load();
        Assert.NotNull(savedSettings.TelemetryClientId);
        Assert.NotEqual(Guid.Empty, savedSettings.TelemetryClientId.Value);

        telemetryService.Shutdown();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}


