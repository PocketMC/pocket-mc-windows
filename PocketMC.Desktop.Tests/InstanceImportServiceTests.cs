using PocketMC.Domain.Models;
using PocketMC.Domain.Models;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Shell;

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
    public async Task ReconstructStagedImportAsync_JavaRestoresPackagedAddon()
    {
        string zipPath = Path.Combine(_root, "java-packaged-addon.zip");
        CreateValidImportZip(zipPath);
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") });

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
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") });

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
    public async Task ImportAsync_PackagedAddonMissing_RecordsFailureInReportAndCreatesInstance()
    {
        string zipPath = Path.Combine(_root, "missing-addon-import.zip");
        CreateValidImportZip(zipPath, includeAddonFile: false);
        InstanceImportService service = CreateService(
            softwareProviders: new[] { new FakeSoftwareProvider("Paper (Test)", "server jar") });

        InstanceImportResult result = await service.ImportAsync(new InstanceImportRequest
        {
            ZipPath = zipPath
        });

        Assert.Equal(Path.Combine(_root, "servers", "import-test"), result.InstancePath);
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "server.jar")));
        Assert.False(File.Exists(Path.Combine(result.InstancePath, "plugins", "Essentials.jar")));
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "import_report.json")));

        Assert.NotNull(result.Report);
        Assert.Equal(1, result.Report.TotalAddons);
        Assert.Equal(0, result.Report.SuccessfulAddons);
        Assert.Equal(1, result.Report.FailedAddons);

        var failedAddon = result.Report.Addons.Single();
        Assert.Equal("Essentials", failedAddon.Name);
        Assert.False(failedAddon.Success);
        Assert.Equal("failed", failedAddon.Status);

        Assert.Equal(result.InstancePath, _lastRegistry!.GetPath(result.InstanceId));
    }

    [Fact]
    public async Task ImportLocalFolderAsync_CopyFiles_CopiesFilesAndRegistersInstance()
    {
        string sourceDir = Path.Combine(_root, "source-folder-copy");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "eula.txt"), "eula=false");
        File.WriteAllText(Path.Combine(sourceDir, "server.properties"), "max-players=20");
        File.WriteAllText(Path.Combine(sourceDir, "some-mod.jar"), "mod-content");

        InstanceImportService service = CreateService();

        var result = await service.ImportLocalFolderAsync(new LocalFolderImportRequest
        {
            SourceFolderPath = sourceDir,
            RequestedName = "imported-folder-copy",
            ServerType = "Vanilla",
            MinecraftVersion = "1.20.1",
            CopyFiles = true,
            Description = "Copied folder description"
        });

        Assert.Equal(Path.Combine(_root, "servers", "imported-folder-copy"), result.InstancePath);
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "eula.txt")));
        Assert.Equal("eula=true", File.ReadAllText(Path.Combine(result.InstancePath, "eula.txt")));
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "server.jar")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "some-mod.jar")));

        var metadata = result.Metadata;
        Assert.Equal("imported-folder-copy", metadata.Name);
        Assert.Equal("Vanilla", metadata.ServerType);
        Assert.Equal("1.20.1", metadata.MinecraftVersion);
        Assert.Equal(20, metadata.MaxPlayers);
    }

    [Fact]
    public async Task ImportLocalFolderAsync_CopyFiles_WhenConfigurationFails_KeepsSourceAndCleansDestination()
    {
        string sourceDir = Path.Combine(_root, "source-folder-copy-failure");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, ".pocket-mc.json"));
        File.WriteAllText(Path.Combine(sourceDir, "eula.txt"), "eula=false");
        File.WriteAllText(Path.Combine(sourceDir, "server.properties"), "max-players=20");
        File.WriteAllText(Path.Combine(sourceDir, "some-mod.jar"), "mod-content");

        InstanceImportService service = CreateService();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.ImportLocalFolderAsync(new LocalFolderImportRequest
            {
                SourceFolderPath = sourceDir,
                RequestedName = "imported-folder-copy-failure",
                ServerType = "Vanilla",
                MinecraftVersion = "1.20.1",
                CopyFiles = true
            }));

        Assert.True(Directory.Exists(sourceDir));
        Assert.True(Directory.Exists(Path.Combine(sourceDir, ".pocket-mc.json")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "server.properties")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "some-mod.jar")));
        Assert.False(Directory.Exists(Path.Combine(_root, "servers", "imported-folder-copy-failure")));
    }

    [Fact]
    public async Task ImportLocalFolderAsync_MoveFiles_MovesFilesAndRegistersInstance()
    {
        string sourceDir = Path.Combine(_root, "source-folder-move");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "eula.txt"), "eula=false");
        File.WriteAllText(Path.Combine(sourceDir, "server.properties"), "max-players=10");
        File.WriteAllText(Path.Combine(sourceDir, "some-mod.jar"), "mod-content");

        InstanceImportService service = CreateService();

        var result = await service.ImportLocalFolderAsync(new LocalFolderImportRequest
        {
            SourceFolderPath = sourceDir,
            RequestedName = "imported-folder-move",
            ServerType = "Vanilla",
            MinecraftVersion = "1.20.1",
            CopyFiles = false,
            Description = "Moved folder description"
        });

        Assert.Equal(Path.Combine(_root, "servers", "imported-folder-move"), result.InstancePath);
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "eula.txt")));
        Assert.True(File.Exists(Path.Combine(result.InstancePath, "server.jar")));

        Assert.False(File.Exists(Path.Combine(sourceDir, "some-mod.jar")));
        Assert.False(File.Exists(Path.Combine(sourceDir, "server.properties")));
    }

    [Fact]
    public async Task ImportLocalFolderAsync_MoveFiles_WhenProgressThrowsAfterMove_RestoresSourceFolder()
    {
        string sourceDir = Path.Combine(_root, "source-folder-progress-failure");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "eula.txt"), "eula=false");
        File.WriteAllText(Path.Combine(sourceDir, "server.properties"), "max-players=10");
        File.WriteAllText(Path.Combine(sourceDir, "some-mod.jar"), "mod-content");

        InstanceImportService service = CreateService();
        var progress = new ThrowOnProgressStep("Moved server files.");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportLocalFolderAsync(new LocalFolderImportRequest
            {
                SourceFolderPath = sourceDir,
                RequestedName = "imported-folder-progress-failure",
                ServerType = "Vanilla",
                MinecraftVersion = "1.20.1",
                CopyFiles = false
            }, progress));

        Assert.Equal("progress failure", ex.Message);
        Assert.True(Directory.Exists(sourceDir));
        Assert.True(File.Exists(Path.Combine(sourceDir, "eula.txt")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "server.properties")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "some-mod.jar")));
        Assert.False(Directory.Exists(Path.Combine(_root, "servers", "imported-folder-progress-failure")));
    }

    private InstanceImportService CreateService(
        IReadOnlyList<IServerSoftwareProvider>? softwareProviders = null,
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
            downloader,
            addonManifestService,
            state,
            NullLogger<InstanceImportService>.Instance);
    }

    private static void CreateValidImportZip(string zipPath, bool includeAddonFile = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddEntry(archive, "manifest.json", CreateValidManifestJson());
        AddEntry(archive, "pocket-mc.json", "{\"name\":\"Import Test\"}");
        AddEntry(archive, "server/server.properties", "motd=Import Test");
        AddEntry(archive, "server/world/level.dat", "world");
        if (includeAddonFile)
        {
            AddEntry(archive, "server/plugins/Essentials.jar", "addon jar");
        }
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
              "versionId": "1.3",
              "fileName": "Essentials.jar",
              "relativePath": "plugins/Essentials.jar",
              "packagedPath": "server/plugins/Essentials.jar"
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
            string? loaderVersion = null,
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
            string? loaderVersion = null,
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

    private sealed class ThrowOnProgressStep : IProgress<InstanceTransferProgress>
    {
        private readonly string _step;
        private bool _hasThrown;

        public ThrowOnProgressStep(string step)
        {
            _step = step;
        }

        public void Report(InstanceTransferProgress value)
        {
            if (!_hasThrown && string.Equals(value.CurrentStep, _step, StringComparison.Ordinal))
            {
                _hasThrown = true;
                throw new InvalidOperationException("progress failure");
            }
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



