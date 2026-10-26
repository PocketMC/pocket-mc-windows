using PocketMC.Infrastructure.Telemetry;
using PocketMC.Infrastructure.Configuration;
using Xunit;
using Moq;
using PocketMC.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace PocketMC.Infrastructure.Tests.Telemetry;

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

    [Fact]
    public async Task FirstRun_ReportsInstallationAndCountry_WhenUsageTelemetryIsDisabled()
    {
        var settings = CreateSettings();
        var requests = new ConcurrentQueue<RecordedRequest>();
        using var telemetry = CreateService(settings, requests);

        await telemetry.ReportOnceAsync();
        Assert.True(settings.Load().HasReportedInstall);
        Assert.False(settings.Load().EnableTelemetry);
        var install = Assert.Single(requests.Where(r => r.Method == HttpMethod.Post));
        Assert.Equal("/api/telemetry/install", install.Uri.AbsolutePath);
        using var payload = JsonDocument.Parse(install.Body);
        Assert.Equal("Test Country", payload.RootElement.GetProperty("country").GetString());
        Assert.Equal(settings.Load().TelemetryClientId.ToString(), payload.RootElement.GetProperty("clientId").GetString());
        Assert.All(requests, r => Assert.Equal("https", r.Uri.Scheme));

        // Acknowledged installations do not send another report or country lookup.
        await telemetry.ReportOnceAsync();
        await telemetry.ReportServerActionAsync("create");
        telemetry.Shutdown();
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task RequiredCountryLookup_UsesHttpsFallback_WhenPrimaryLookupFails()
    {
        var settings = CreateSettings();
        var requests = new ConcurrentQueue<RecordedRequest>();
        using var telemetry = CreateService(settings, requests, request => request.Uri.AbsolutePath switch
        {
            "/json/" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "/country_name/" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fallback Country") },
            _ => new HttpResponseMessage(HttpStatusCode.OK)
        });

        await telemetry.ReportOnceAsync();

        Assert.True(settings.Load().HasReportedInstall);
        Assert.Equal(new[] { "/json/", "/country_name/", "/api/telemetry/install" }, requests.Select(r => r.Uri.AbsolutePath));
        Assert.All(requests, r => Assert.Equal("https", r.Uri.Scheme));
        using var payload = JsonDocument.Parse(requests.Last().Body);
        Assert.Equal("Fallback Country", payload.RootElement.GetProperty("country").GetString());
    }

    [Fact]
    public async Task RequiredInstallation_RetriesAfterFailure_WithoutEnablingUsageTelemetry()
    {
        var settings = CreateSettings();
        var requests = new ConcurrentQueue<RecordedRequest>();
        bool installSucceeds = false;
        using var telemetry = CreateService(settings, requests, request => request.Method == HttpMethod.Get
            ? CountryResponse()
            : new HttpResponseMessage(installSucceeds ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable));

        await telemetry.ReportOnceAsync();
        Assert.False(settings.Load().HasReportedInstall);
        int failedAttempts = requests.Count(r => r.Method == HttpMethod.Post);
        Assert.True(failedAttempts > 0);

        installSucceeds = true;
        await telemetry.ReportOnceAsync();
        await telemetry.ReportOnceAsync();

        Assert.True(settings.Load().HasReportedInstall);
        Assert.False(settings.Load().EnableTelemetry);
        Assert.Single(requests.Where(r => r.Method == HttpMethod.Get));
        Assert.Equal(failedAttempts + 1, requests.Count(r => r.Method == HttpMethod.Post));
        Assert.DoesNotContain(requests, r => r.Uri.AbsolutePath == "/api/telemetry/report");
    }

    [Fact]
    public async Task InstallationAcknowledgment_PreservesPreferencesChangedDuringTheRequest()
    {
        var settings = CreateSettings(enableUsage: true);
        var requests = new ConcurrentQueue<RecordedRequest>();
        using var telemetry = CreateService(settings, requests, request =>
        {
            if (request.Method == HttpMethod.Get) return CountryResponse();
            var changed = settings.Load();
            changed.EnableTelemetry = false;
            changed.ConsoleBufferSize = 1234;
            settings.Save(changed);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await telemetry.ReportOnceAsync();

        var saved = settings.Load();
        Assert.True(saved.HasReportedInstall);
        Assert.False(saved.EnableTelemetry);
        Assert.Equal(1234, saved.ConsoleBufferSize);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task EnabledUsageTelemetry_ReportsHeartbeatAndServerActions_AfterInstallation()
    {
        var settings = CreateSettings(enableUsage: true, installReported: true);
        var requests = new ConcurrentQueue<RecordedRequest>();
        using var telemetry = CreateService(settings, requests);

        await telemetry.ReportOnceAsync();
        await telemetry.ReportServerActionAsync("create");

        Assert.Equal(new[] { "/json/", "/api/telemetry/report", "/api/telemetry/server-action" }, requests.Select(r => r.Uri.AbsolutePath));
    }

    private SettingsManager CreateSettings(bool enableUsage = false, bool installReported = false)
    {
        var settings = new SettingsManager(Path.Combine(_tempDirectory, "settings.json"));
        settings.Save(new AppSettings
        {
            TelemetryClientId = Guid.NewGuid(),
            EnableTelemetry = enableUsage,
            HasReportedInstall = installReported
        });
        return settings;
    }

    private static TelemetryService CreateService(SettingsManager settings, ConcurrentQueue<RecordedRequest> requests,
        Func<RecordedRequest, HttpResponseMessage>? respond = null)
    {
        respond ??= request => request.Method == HttpMethod.Get ? CountryResponse() : new HttpResponseMessage(HttpStatusCode.OK);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new RecordingHandler(requests, respond)));
        var processes = new ServerProcessManager(null!, null!, null!, null!, null!, null!, null!);
        return new TelemetryService(settings, processes, null!, factory.Object, NullLogger<TelemetryService>.Instance);
    }

    private static HttpResponseMessage CountryResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"country_name\":\"Test Country\"}", Encoding.UTF8, "application/json")
    };

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
    private sealed class RecordingHandler(ConcurrentQueue<RecordedRequest> requests, Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new RecordedRequest(request.Method, request.RequestUri!, body);
            requests.Enqueue(recorded);
            return respond(recorded);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

