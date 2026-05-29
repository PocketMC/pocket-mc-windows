using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC_Import_" + Guid.NewGuid().ToString("N"));
    private InstanceRegistry? _lastRegistry;

    [Fact]
    public async Task StageImportAsync_ValidPackage_ExtractsIntoHiddenStagingDirectory()
    {
        string zipPath = Path.Combine(_root, "valid-import.zip");
        CreateValidImportZip(zipPath);
        InstanceImportService service = CreateService();

        InstanceImportStagingResult result = await service.StageImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        string serversRoot = Path.Combine(_root, "servers");
        string stagingRoot = Path.Combine(serversRoot, ".staging");

        Assert.StartsWith(stagingRoot, result.StagingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(result.StagingDirectory));
        Assert.True(Directory.Exists(result.ServerDirectory));
        Assert.True(File.Exists(Path.Combine(result.StagingDirectory, "manifest.json")));
        Assert.True(File.Exists(result.MetadataPath));
        Assert.True(File.Exists(Path.Combine(result.ServerDirectory, "server.properties")));
        Assert.Equal("Import Test", result.Manifest.ServerMeta.Name);
        Assert.Equal(InstanceServerPlatform.Java, result.Manifest.Software.Platform);

        Assert.Empty(Directory.GetDirectories(serversRoot).Where(path =>
            !Path.GetFileName(path).Equals(".staging", StringComparison.OrdinalIgnoreCase)));

        if (OperatingSystem.IsWindows())
        {
            Assert.True(new DirectoryInfo(stagingRoot).Attributes.HasFlag(FileAttributes.Hidden));
            Assert.True(new DirectoryInfo(result.StagingDirectory).Attributes.HasFlag(FileAttributes.Hidden));
        }
    }

    [Fact]
    public async Task ReadManifestAsync_RejectsInvalidZipHeader()
    {
        Directory.CreateDirectory(_root);
        string zipPath = Path.Combine(_root, "not-a-zip.zip");
        await File.WriteAllTextAsync(zipPath, "not a zip");
        InstanceImportService service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadManifestAsync(zipPath));
    }

    [Fact]
    public async Task StageImportAsync_ZipSlipEntry_DeletesOperationStagingFolder()
    {
        string zipPath = Path.Combine(_root, "malicious-import.zip");
        Directory.CreateDirectory(_root);

        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "manifest.json", CreateValidManifestJson());
            AddEntry(archive, "pocket-mc.json", "{}");
            AddEntry(archive, "server/server.properties", "motd=Import Test");
            AddEntry(archive, "../outside.txt", "bad");
        }

        InstanceImportService service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        }));

        string stagingRoot = Path.Combine(_root, "servers", ".staging");
        Assert.False(File.Exists(Path.Combine(_root, "servers", "outside.txt")));
        Assert.True(!Directory.Exists(stagingRoot) || !Directory.EnumerateDirectories(stagingRoot).Any());
    }

    [Fact]
    public async Task ReadManifestAsync_RejectsMismatchedRuntimeForBedrock()
    {
        string zipPath = Path.Combine(_root, "bad-manifest.zip");
        Directory.CreateDirectory(_root);

        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "manifest.json", """
                {
                  "exportVersion": "1.0",
                  "origin": { "pocketMcVersion": "2.4.0", "timestamp": "2026-05-29T00:00:00Z" },
                  "serverMeta": { "name": "Bad Bedrock", "description": "", "icon": null },
                  "software": {
                    "platform": "Bedrock",
                    "type": "BDS",
                    "minecraftVersion": "1.20.80",
                    "loaderVersion": null
                  },
                  "runtime": { "type": "Java", "targetVersion": "21" },
                  "addons": []
                }
                """);
            AddEntry(archive, "pocket-mc.json", "{}");
            AddEntry(archive, "server/server.properties", "level-name=Bedrock level");
        }

        InstanceImportService service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadManifestAsync(zipPath));
    }

    [Fact]
    public async Task ReconstructStagedImportAsync_JavaDownloadsServerJarAndRemoteAddon()
    {
        string zipPath = Path.Combine(_root, "java-remote-addon.zip");
        CreateValidImportZip(zipPath);
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") },
            addonProviders: new[] { new FakeAddonProvider("Modrinth") },
            httpResponses: new Dictionary<string, byte[]>
            {
                ["https://downloads.test/Essentials.jar"] = "addon jar"u8.ToArray()
            });

        InstanceImportStagingResult staged = await service.StageImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        await service.ReconstructStagedImportAsync(staged);

        Assert.Equal("server jar", File.ReadAllText(Path.Combine(staged.ServerDirectory, "server.jar")));
        Assert.Equal("addon jar", File.ReadAllText(Path.Combine(staged.ServerDirectory, "plugins", "Essentials.jar")));
        Assert.True(File.Exists(Path.Combine(staged.ServerDirectory, "addon_manifest.json")));
    }

    [Fact]
    public async Task ReconstructStagedImportAsync_JavaFallsBackToProjectLookupWhenExactVersionLookupFails()
    {
        string zipPath = Path.Combine(_root, "java-project-fallback.zip");
        CreateValidImportZip(zipPath);
        var provider = new FallbackAddonProvider(
            "Modrinth",
            versionById: null,
            latestVersion: CreateAddonVersion("essentials", "latest", "Essentials.jar", "https://downloads.test/Essentials.jar"));
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") },
            addonProviders: new IAddonProvider[] { provider },
            httpResponses: new Dictionary<string, byte[]>
            {
                ["https://downloads.test/Essentials.jar"] = "addon jar"u8.ToArray()
            });

        InstanceImportStagingResult staged = await service.StageImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        await service.ReconstructStagedImportAsync(staged);

        Assert.Equal(1, provider.VersionByIdCalls);
        Assert.True(provider.LatestVersionCalls >= 1);
        Assert.Equal("addon jar", File.ReadAllText(Path.Combine(staged.ServerDirectory, "plugins", "Essentials.jar")));
    }

    [Fact]
    public async Task ReconstructStagedImportAsync_JavaTriesNextCandidateWhenExactDownloadUrlFails()
    {
        string zipPath = Path.Combine(_root, "java-download-fallback.zip");
        CreateValidImportZip(zipPath);
        var provider = new FallbackAddonProvider(
            "Modrinth",
            versionById: CreateAddonVersion("essentials", "1.3", "Essentials-bad.jar", "https://downloads.test/missing.jar"),
            latestVersion: CreateAddonVersion("essentials", "latest", "Essentials.jar", "https://downloads.test/Essentials.jar"));
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") },
            addonProviders: new IAddonProvider[] { provider },
            httpResponses: new Dictionary<string, byte[]>
            {
                ["https://downloads.test/Essentials.jar"] = "addon jar"u8.ToArray()
            });

        InstanceImportStagingResult staged = await service.StageImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        await service.ReconstructStagedImportAsync(staged);

        Assert.Equal(1, provider.VersionByIdCalls);
        Assert.True(provider.LatestVersionCalls >= 1);
        Assert.False(File.Exists(Path.Combine(staged.ServerDirectory, "plugins", "Essentials-bad.jar")));
        Assert.Equal("addon jar", File.ReadAllText(Path.Combine(staged.ServerDirectory, "plugins", "Essentials.jar")));
    }

    [Fact]
    public async Task ReconstructStagedImportAsync_BedrockExtractsBdsWithoutOverwritingImportedConfig()
    {
        string zipPath = Path.Combine(_root, "bedrock-import.zip");
        CreateBedrockImportZip(zipPath);
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeBdsProvider() });

        InstanceImportStagingResult staged = await service.StageImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        await service.ReconstructStagedImportAsync(staged);

        Assert.True(File.Exists(Path.Combine(staged.ServerDirectory, "bedrock_server.exe")));
        Assert.Equal("level-name=Imported", File.ReadAllText(Path.Combine(staged.ServerDirectory, "server.properties")));
    }

    [Fact]
    public async Task ImportAsync_Success_PromotesOnlyCompletedStagingAndCleansStagingFolder()
    {
        string zipPath = Path.Combine(_root, "complete-import.zip");
        CreateValidImportZip(zipPath);
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") },
            addonProviders: new[] { new FakeAddonProvider("Modrinth") },
            httpResponses: new Dictionary<string, byte[]>
            {
                ["https://downloads.test/Essentials.jar"] = "addon jar"u8.ToArray()
            });

        InstanceImportResult result = await service.ImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        Assert.Equal(Path.Combine(_root, "servers", "import-test"), result.InstancePath);
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "server.jar")));
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "plugins", "Essentials.jar")));
        Assert.True(File.Exists(Path.Combine(result.InstancePath, ".pocket-mc.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "servers", ".staging")));
        Assert.Equal(result.InstancePath, _lastRegistry!.GetPath(result.InstanceId));
        Assert.Equal("Paper", result.Metadata.ServerType);
    }

    [Fact]
    public async Task ImportAsync_ScrubsBackupHistoryAndCustomBackupDirectory()
    {
        string zipPath = Path.Combine(_root, "backup-history-import.zip");
        string originalCustomBackupDirectory = Path.Combine(_root, "original-custom-backups");
        Directory.CreateDirectory(originalCustomBackupDirectory);
        File.WriteAllText(Path.Combine(originalCustomBackupDirectory, "world-2026-05-29-13-17-37.zip"), "original backup");
        CreateBackupStateImportZip(zipPath, originalCustomBackupDirectory);

        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") });

        InstanceImportResult result = await service.ImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath,
            RequestedName = "Imported Backup State"
        });

        Assert.Null(result.Metadata.LastBackupTime);
        Assert.Null(result.Metadata.CustomBackupDirectory);
        Assert.Equal(12, result.Metadata.BackupIntervalHours);
        Assert.False(Directory.Exists(Path.Combine(result.InstancePath, "backups")));

        string metadataJson = await File.ReadAllTextAsync(Path.Combine(result.InstancePath, ".pocket-mc.json"));
        InstanceMetadata persistedMetadata = JsonSerializer.Deserialize<InstanceMetadata>(metadataJson)!;
        Assert.Null(persistedMetadata.LastBackupTime);
        Assert.Null(persistedMetadata.CustomBackupDirectory);
    }

    [Fact]
    public async Task ImportAsync_RemoteAddonUnavailable_CleansStagingAndDoesNotCreateActiveInstance()
    {
        string zipPath = Path.Combine(_root, "missing-addon-import.zip");
        CreateValidImportZip(zipPath);
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") },
            addonProviders: new[] { new FakeAddonProvider("Modrinth") });

        await Assert.ThrowsAsync<AddonUnavailableException>(() => service.ImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        }));

        string serversRoot = Path.Combine(_root, "servers");
        Assert.False(Directory.Exists(Path.Combine(serversRoot, ".staging")));
        Assert.False(Directory.Exists(Path.Combine(serversRoot, "import-test")));
        Assert.Empty(_lastRegistry!.GetAll());
    }

    private InstanceImportService CreateService(
        IReadOnlyList<IServerSoftwareProvider>? softwareProviders = null,
        IReadOnlyList<IAddonProvider>? addonProviders = null,
        IReadOnlyDictionary<string, byte[]>? httpResponses = null)
    {
        var state = new ApplicationState();
        state.ApplySettings(new AppSettings { AppRootPath = _root });
        var pathService = new InstancePathService(state);
        var registry = new InstanceRegistry(pathService, NullLogger<InstanceRegistry>.Instance);
        _lastRegistry = registry;
        var downloader = new DownloaderService(
            new FakeHttpClientFactory(httpResponses ?? new Dictionary<string, byte[]>()),
            NullLogger<DownloaderService>.Instance);
        var addonManifestService = new AddonManifestService();

        return new InstanceImportService(
            pathService,
            registry,
            softwareProviders ?? Array.Empty<IServerSoftwareProvider>(),
            addonProviders ?? Array.Empty<IAddonProvider>(),
            downloader,
            new MarketplaceFileInstaller(downloader, NullLogger<MarketplaceFileInstaller>.Instance),
            addonManifestService,
            new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance),
            state,
            NullLogger<InstanceImportService>.Instance);
    }

    private static void CreateValidImportZip(string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddEntry(archive, "manifest.json", CreateValidManifestJson());
        AddEntry(archive, "pocket-mc.json", "{\"name\":\"Import Test\"}");
        AddEntry(archive, "server/server.properties", "motd=Import Test");
        AddEntry(archive, "server/world/level.dat", "world");
    }

    private static void CreateBedrockImportZip(string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddEntry(archive, "manifest.json", """
            {
              "exportVersion": "1.0",
              "origin": { "pocketMcVersion": "2.4.0", "timestamp": "2026-05-29T00:00:00Z" },
              "serverMeta": { "name": "Bedrock Import", "description": "Desc", "icon": null },
              "software": {
                "platform": "Bedrock",
                "type": "BDS",
                "minecraftVersion": "1.20.80",
                "loaderVersion": null
              },
              "runtime": { "type": "Native" },
              "addons": []
            }
            """);
        AddEntry(archive, "pocket-mc.json", "{\"name\":\"Bedrock Import\"}");
        AddEntry(archive, "server/server.properties", "level-name=Imported");
        AddEntry(archive, "server/worlds/Imported/level.dat", "world");
    }

    private static void CreateBackupStateImportZip(string zipPath, string customBackupDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        var metadata = new InstanceMetadata
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Original Backup State",
            ServerType = "Paper",
            MinecraftVersion = "1.20.4",
            BackupIntervalHours = 12,
            LastBackupTime = new DateTime(2026, 5, 29, 8, 0, 0, DateTimeKind.Utc),
            CustomBackupDirectory = customBackupDirectory
        };

        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddEntry(archive, "manifest.json", """
            {
              "exportVersion": "1.0",
              "origin": { "pocketMcVersion": "2.4.0", "timestamp": "2026-05-29T00:00:00Z" },
              "serverMeta": { "name": "Original Backup State", "description": "Desc", "icon": null },
              "software": {
                "platform": "Java",
                "type": "Paper",
                "minecraftVersion": "1.20.4",
                "loaderVersion": null
              },
              "runtime": { "type": "Java", "targetVersion": "17" },
              "addons": []
            }
            """);
        AddEntry(archive, "pocket-mc.json", JsonSerializer.Serialize(metadata));
        AddEntry(archive, "server/server.properties", "motd=Backup State");
        AddEntry(archive, "server/world/level.dat", "world");
        AddEntry(archive, "server/backups/world-2026-05-29-13-17-37.zip", "legacy backup");
        AddEntry(archive, "server/backups/backup-manifest.json", """
            {
              "entries": [
                { "fileName": "world-2026-05-29-13-17-37.zip", "version": 1 }
              ]
            }
            """);
    }

    private static string CreateValidManifestJson() => """
        {
          "exportVersion": "1.0",
          "origin": { "pocketMcVersion": "2.4.0", "timestamp": "2026-05-29T00:00:00Z" },
          "serverMeta": { "name": "Import Test", "description": "Desc", "icon": null },
          "software": {
            "platform": "Java",
            "type": "Paper",
            "minecraftVersion": "1.20.4",
            "loaderVersion": null
          },
          "runtime": { "type": "Java", "targetVersion": "17" },
          "addons": [
            {
              "name": "Essentials",
              "type": "plugin",
              "provider": "Modrinth",
              "projectId": "essentials",
              "versionId": "1.3"
            }
          ]
        }
        """;

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static MarketplaceVersion CreateAddonVersion(
        string projectId,
        string versionId,
        string fileName,
        string downloadUrl) => new()
    {
        Id = versionId,
        ProjectId = projectId,
        ProjectTitle = "Essentials",
        FileName = fileName,
        DownloadUrl = downloadUrl,
        Hash = null,
        HashType = null,
        SelectedLoader = "paper"
    };

    private sealed class FakeSoftwareProvider : IServerSoftwareProvider
    {
        private readonly string _contents;

        public FakeSoftwareProvider(string displayName, string contents)
        {
            DisplayName = displayName;
            _contents = contents;
        }

        public string DisplayName { get; }

        public Task<List<MinecraftVersion>> GetAvailableVersionsAsync() => Task.FromResult(new List<MinecraftVersion>());

        public async Task DownloadSoftwareAsync(
            string versionId,
            string destinationPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, _contents, cancellationToken);
            progress?.Report(new DownloadProgress { BytesRead = _contents.Length, TotalBytes = _contents.Length });
        }
    }

    private sealed class FakeBdsProvider : IServerSoftwareProvider
    {
        public string DisplayName => "Bedrock (BDS Test)";

        public Task<List<MinecraftVersion>> GetAvailableVersionsAsync() => Task.FromResult(new List<MinecraftVersion>());

        public Task DownloadSoftwareAsync(
            string versionId,
            string destinationPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using ZipArchive archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
            AddEntry(archive, "bedrock_server.exe", "bds exe");
            AddEntry(archive, "server.properties", "level-name=Default");
            progress?.Report(new DownloadProgress { BytesRead = 1, TotalBytes = 1 });
            return Task.CompletedTask;
        }
    }

    private sealed class FallbackAddonProvider : IAddonProvider
    {
        private readonly MarketplaceVersion? _versionById;
        private readonly MarketplaceVersion? _latestVersion;

        public FallbackAddonProvider(
            string name,
            MarketplaceVersion? versionById,
            MarketplaceVersion? latestVersion)
        {
            Name = name;
            _versionById = versionById;
            _latestVersion = latestVersion;
        }

        public string Name { get; }
        public int VersionByIdCalls { get; private set; }
        public int LatestVersionCalls { get; private set; }

        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader)
        {
            LatestVersionCalls++;
            return Task.FromResult(_latestVersion);
        }

        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates)
        {
            LatestVersionCalls++;
            return Task.FromResult(_latestVersion);
        }

        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId)
        {
            VersionByIdCalls++;
            return Task.FromResult(_versionById);
        }

        public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId) =>
            Task.FromResult<MarketplaceProjectInfo?>(new MarketplaceProjectInfo { Id = projectId, Title = "Essentials" });
    }

    private sealed class FakeAddonProvider : IAddonProvider
    {
        public FakeAddonProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader) =>
            Task.FromResult<MarketplaceVersion?>(CreateVersion(projectId, "latest"));

        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates) =>
            Task.FromResult<MarketplaceVersion?>(CreateVersion(projectId, "latest"));

        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId) =>
            Task.FromResult<MarketplaceVersion?>(CreateVersion("essentials", versionId));

        public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId) =>
            Task.FromResult<MarketplaceProjectInfo?>(new MarketplaceProjectInfo { Id = projectId, Title = "Essentials" });

        private static MarketplaceVersion CreateVersion(string projectId, string versionId) => new()
        {
            Id = versionId,
            ProjectId = projectId,
            ProjectTitle = "Essentials",
            FileName = "Essentials.jar",
            DownloadUrl = "https://downloads.test/Essentials.jar",
            Hash = null,
            HashType = null,
            SelectedLoader = "paper"
        };
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly IReadOnlyDictionary<string, byte[]> _responses;

        public FakeHttpClientFactory(IReadOnlyDictionary<string, byte[]> responses)
        {
            _responses = responses;
        }

        public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler(_responses));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> _responses;

        public FakeHttpMessageHandler(IReadOnlyDictionary<string, byte[]> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string key = request.RequestUri?.ToString() ?? string.Empty;
            if (!_responses.TryGetValue(key, out byte[]? body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }

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
}
