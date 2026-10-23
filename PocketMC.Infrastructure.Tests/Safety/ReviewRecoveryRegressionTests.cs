using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Players;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Backups;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Power;

namespace PocketMC.Infrastructure.Tests.Safety;

public sealed class ReviewRecoveryRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC-Failure-" + Guid.NewGuid().ToString("N"));
    public ReviewRecoveryRegressionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string Write(string relative, string content)
    { string path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path; }
    private BackupService Backups()
    {
        var job = (JobObject)RuntimeHelpers.GetUninitializedObject(typeof(JobObject));
        var processes = new ServerProcessManager(job, null!, null!, null!, new PlayerListParser(), NullLogger<ServerProcessManager>.Instance, NullLoggerFactory.Instance);
        var settings = new SettingsManager(Path.Combine(_root, "settings.json"));
        var history = new CloudBackupUploadHistoryStore(NullLogger<CloudBackupUploadHistoryStore>.Instance, Path.Combine(_root, "history.json"));
        return new BackupService(processes, new ServerConfigurationService(null!, null!), settings,
            new CloudBackupService(Array.Empty<PocketMC.Application.Interfaces.Backups.ICloudBackupProvider>(), history, settings, NullLogger<CloudBackupService>.Instance), NullLogger<BackupService>.Instance);
    }
    private static InstanceMetadata Metadata() => new() { Id = Guid.NewGuid(), Name = "Recovery", ServerType = "Paper", MaxBackupsToKeep = 1 };

    [Fact]
    public async Task Backup_IncludesEveryPaperDimension()
    {
        Write("server.properties", "level-name=custom\n");
        Write("custom/level.dat", "overworld"); Write("custom_nether/level.dat", "nether"); Write("custom_the_end/level.dat", "end");
        await Backups().RunBackupAsync(Metadata(), _root);
        using var zip = ZipFile.OpenRead(Directory.GetFiles(Path.Combine(_root, "backups"), "*.zip").Single());
        Assert.NotNull(zip.GetEntry("custom/level.dat")); Assert.NotNull(zip.GetEntry("custom_nether/level.dat")); Assert.NotNull(zip.GetEntry("custom_the_end/level.dat"));
    }
    [Fact]
    public async Task UnreadableRequiredRegion_AbortsBackupWithoutPromotingArchive()
    {
        Write("world/level.dat", "world"); string file = Write("world/region/r.0.0.mca", "required");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAnyAsync<IOException>(() => Backups().RunBackupAsync(Metadata(), _root));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "backups"), "*.zip"));
        Assert.Empty(BackupManifest.Load(_root).Entries);
    }
    [Fact]
    public async Task SharedBackupDestination_DoesNotPruneAnotherInstancesArchives()
    {
        string a = Path.Combine(_root, "a"), b = Path.Combine(_root, "b"), shared = Path.Combine(_root, "shared");
        Write("a/world/level.dat", "a"); Write("b/world/level.dat", "b");
        var first = Metadata(); var second = Metadata(); first.CustomBackupDirectory = shared; second.CustomBackupDirectory = shared;
        var backups = Backups();
        await backups.RunBackupAsync(first, a); await backups.RunBackupAsync(second, b); await backups.RunBackupAsync(first, a);
        var names = Directory.GetFiles(shared, "*.zip").Select(Path.GetFileName).ToArray();
        Assert.Equal(2, names.Length);
        Assert.Contains(names, name => name!.Contains(first.Id.ToString("N")));
        Assert.Contains(names, name => name!.Contains(second.Id.ToString("N")));
    }
    [Fact]
    public async Task NestedLegacyArchive_RestoresAtWorldRoot()
    {
        string input = Path.Combine(_root, "nested.zip");
        using (var zip = ZipFile.Open(input, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("wrapper/level.dat").Open())) writer.Write("restored");
        await Backups().RestoreBackupAsync(Metadata(), input, _root);
        Assert.Equal("restored", File.ReadAllText(Path.Combine(_root, "world/level.dat")));
        Assert.False(Directory.Exists(Path.Combine(_root, "world/wrapper")));
    }
    [Fact]
    public async Task InvalidContentRange_DoesNotReplaceWorkingArtifact()
    {
        string destination = Write("server.jar", "working");
        Write("server.jar.partial", "OLD");
        Write("server.jar.partial.identity", "https://example.invalid/server");
        var factory = new FakeFactory(() => { var r = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StringContent("FULL") }; r.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 3, 4); return r; });
        var downloader = new DownloaderService(factory, NullLogger<DownloaderService>.Instance);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("OLDFULL")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadFileAsync("https://example.invalid/server", destination, hash));
        Assert.Equal("working", File.ReadAllText(destination));
    }
    [Fact]
    public async Task EmptyDownload_DoesNotReplaceWorkingArtifact()
    {
        string destination = Write("server.jar", "working");
        var downloader = new DownloaderService(new FakeFactory(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) }), NullLogger<DownloaderService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadFileAsync("https://example.invalid/server", destination));
        Assert.Equal("working", File.ReadAllText(destination));
    }
    [Fact]
    public async Task StopDuringProvisioning_DoesNotCreateAProcess()
    {
        var state = new PocketMC.Application.Services.Shell.ApplicationState(); state.Settings.AppRootPath = _root;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provision = new Mock<JavaProvisioningService>(null!, state, null!, null!, null!, NullLogger<JavaProvisioningService>.Instance);
        provision.Setup(p => p.IsJavaVersionPresent(It.IsAny<int>())).Returns(false);
        provision.Setup(p => p.EnsureJavaAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<IProgress<DownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<int, bool, IProgress<DownloadProgress>?, CancellationToken>(async (_, _, _, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); });
        var java = new JavaLaunchConfigurator(provision.Object, new ForgeInstaller(null!), NullLogger<JavaLaunchConfigurator>.Instance);
        var launch = new ServerLaunchConfigurator(java, new BedrockLaunchConfigurator(), new PocketMineLaunchConfigurator(null!, NullLogger<PocketMineLaunchConfigurator>.Instance));
        using var process = new ServerProcess(Guid.NewGuid(), (JobObject)RuntimeHelpers.GetUninitializedObject(typeof(JobObject)), launch, new PlayerListParser(), NullLogger<ServerProcess>.Instance);
        Task starting = process.StartAsync(new InstanceMetadata { ServerType = "Vanilla", MinecraftVersion = "1.21.4" }, _root, _root);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await process.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Null(process.GetInternalProcess()); Assert.Equal(ServerState.Stopped, process.State);
    }
    [Fact]
    public void PowerRequests_AreOwnedByOnePersistentThread()
    {
        var api = new RecordingPowerApi();
        using var service = new SleepPreventionService(api, NullLogger<SleepPreventionService>.Instance);
        var enable = new Thread(service.PreventSleep); enable.Start(); enable.Join();
        var disable = new Thread(service.AllowSleep); disable.Start(); disable.Join();
        Assert.Equal(2, api.Threads.Count); Assert.Single(api.Threads.Distinct());
    }
    [Fact]
    public async Task ConcurrentCloudHistory_PersistsEveryRecord()
    {
        var history = new CloudBackupUploadHistoryStore(NullLogger<CloudBackupUploadHistoryStore>.Instance, Path.Combine(_root, "history.json"));
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => history.AddRecord(new() { LocalBackupFileName = i.ToString() }))));
        Assert.Equal(50, history.Load().Count);
    }
    [Fact]
    public void TelemetryOptOut_MakesNoHttpRequestsIncludingFirstRun()
    {
        var settings = new SettingsManager(Path.Combine(_root, "telemetry-settings.json"));
        settings.Save(new AppSettings { EnableTelemetry = false, HasReportedInstall = false });
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var job = (JobObject)RuntimeHelpers.GetUninitializedObject(typeof(JobObject));
        var processes = new ServerProcessManager(job, null!, null!, null!, new PlayerListParser(), NullLogger<ServerProcessManager>.Instance, NullLoggerFactory.Instance);
        using var telemetry = new PocketMC.Infrastructure.Telemetry.TelemetryService(settings, processes, null!, factory.Object, NullLogger<PocketMC.Infrastructure.Telemetry.TelemetryService>.Instance);
        telemetry.Initialize();
        telemetry.Shutdown(); // Drains the first reporting pass and any pending worker.
        factory.VerifyNoOtherCalls();
        Assert.False(settings.Load().HasReportedInstall);
    }

    [Fact]
    public async Task ShareableExport_ExcludesSecrets_PrivateBackupIsExplicit()
    {
        string instance = Path.Combine(_root, "instance");
        Write("instance/server.properties", "motd=hello # literal\nrcon.password=example-private-password\nenable-rcon=true\n");
        Write("instance/plugins/Example/credentials.yml", "token: example-private-token");
        Write("instance/world/level.dat", "world");
        var state = new PocketMC.Application.Services.Shell.ApplicationState(); state.Settings.AppRootPath = _root;
        var service = new InstanceExportService(new PocketMC.Infrastructure.Marketplace.AddonManifestService(), state, NullLogger<InstanceExportService>.Instance);
        string sharedZip = Path.Combine(_root, "shared.zip");
        await service.ExportAsync(new PocketMC.Application.Interfaces.Instances.InstanceExportRequest { Metadata = Metadata(), InstancePath = instance, DestinationZipPath = sharedZip });
        using (var shared = ZipFile.OpenRead(sharedZip))
        {
            Assert.Null(shared.GetEntry("server/plugins/Example/credentials.yml"));
            using var reader = new StreamReader(shared.GetEntry("server/server.properties")!.Open());
            string properties = await reader.ReadToEndAsync();
            Assert.DoesNotContain("example-private-password", properties);
            Assert.Contains("enable-rcon=false", properties);
            Assert.NotNull(shared.GetEntry("server/world/level.dat"));
        }
        string privateZip = Path.Combine(_root, "private.zip");
        await service.ExportAsync(new PocketMC.Application.Interfaces.Instances.InstanceExportRequest { Metadata = Metadata(), InstancePath = instance, DestinationZipPath = privateZip, IncludeSensitiveConfiguration = true });
        using var backup = ZipFile.OpenRead(privateZip);
        Assert.NotNull(backup.GetEntry("server/plugins/Example/credentials.yml"));
        using var privateReader = new StreamReader(backup.GetEntry("server/server.properties")!.Open());
        Assert.Contains("example-private-password", await privateReader.ReadToEndAsync());
    }

    private sealed class RecordingPowerApi : IExecutionStateApi
    { public List<int> Threads { get; } = new(); public uint SetThreadExecutionState(uint flags) { Threads.Add(Environment.CurrentManagedThreadId); return 1; } }
    private sealed class FakeFactory(Func<HttpResponseMessage> response) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(new FakeHandler(response)); }
    private sealed class FakeHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response()); }
}
