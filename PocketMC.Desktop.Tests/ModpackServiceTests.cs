using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Infrastructure;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests;

public class ModpackServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InstancePathService _pathService;
    private readonly InstanceRegistry _registry;
    private readonly InstanceManager _instanceManager;
    private readonly ApplicationState _appState;
    private readonly ModpackParser _parser;
    private readonly List<HttpRequestMessage> _sentRequests = new();

    public ModpackServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ModpackServiceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _appState = new ApplicationState();
        _appState.ApplySettings(new AppSettings { AppRootPath = _tempDir });
        _pathService = new InstancePathService(_appState);
        _registry = new InstanceRegistry(_pathService, NullLogger<InstanceRegistry>.Instance);
        _instanceManager = new InstanceManager(_registry, _pathService, _appState, null!, NullLogger<InstanceManager>.Instance, null!);
        _parser = new ModpackParser(NullLogger<ModpackParser>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private HttpClient CreateMockHttpClient()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>((req, token) =>
            {
                lock (_sentRequests)
                {
                    _sentRequests.Add(req);
                }
            })
            .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                
                // Return dummy response for CurseForge API and proxy calls
                if (req.RequestUri != null && (req.RequestUri.AbsoluteUri.Contains("api.curseforge.com") || req.RequestUri.AbsoluteUri.Contains("api.curse.tools")))
                {
                    var data = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["downloadUrl"] = "https://example.com/downloaded-mod.jar",
                            ["fileName"] = "downloaded-mod.jar"
                        }
                    };
                    response.Content = new StringContent(JsonSerializer.Serialize(data));
                }
                else
                {
                    response.Content = new ByteArrayContent(new byte[10]);
                }
                return response;
            });

        return new HttpClient(handlerMock.Object);
    }

    [Fact]
    public async Task ImportToExistingInstance_ShouldRollbackMetadataOnFailure()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Download failure"));

        var httpClient = new HttpClient(handlerMock.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var downloader = new DownloaderService(mockFactory.Object, NullLogger<DownloaderService>.Instance);
        var fabric = new FabricProvider(httpClient, downloader);
        var forge = new ForgeProvider(httpClient, downloader);
        var neoforge = new NeoForgeProvider(httpClient, downloader);
        var manifestService = new AddonManifestService();

        var service = new ModpackService(
            httpClient,
            downloader,
            fabric,
            forge,
            neoforge,
            _instanceManager,
            _parser,
            manifestService,
            _appState,
            NullLogger<ModpackService>.Instance
        );

        var pack = new ModpackImportResult
        {
            Name = "TestPack",
            MinecraftVersion = "1.20.2",
            Loader = "Fabric",
            LoaderVersion = "0.15.0"
        };
        pack.Mods.Add(new ModpackFile
        {
            Name = "TestMod",
            DownloadUrl = "https://example.com/mod.jar",
            DestinationPath = "mods/mod.jar"
        });

        var metadata = new InstanceMetadata
        {
            MinecraftVersion = "1.20.1",
            ServerType = "Vanilla",
            LoaderVersion = ""
        };

        // Create dummy instance metadata file
        string metadataPath = _pathService.GetInstancePath(_tempDir);
        Directory.CreateDirectory(metadataPath);
        _instanceManager.SaveMetadata(metadata, _tempDir);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await service.ImportToExistingInstanceAsync(pack, metadata, _tempDir, "dummy.zip", new Progress<PocketMC.Application.Interfaces.Instances.InstanceTransferProgress>());
        });

        // Verify the metadata reference has rolled back values
        Assert.Equal("1.20.1", metadata.MinecraftVersion);
        Assert.Equal("Vanilla", metadata.ServerType);
        Assert.Equal("", metadata.LoaderVersion);
    }

    [Fact]
    public async Task ImportToExistingInstance_ShouldRegisterModsAndDownloadLoaderAndExtractOverrides()
    {
        // Arrange
        var httpClient = CreateMockHttpClient();
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => CreateMockHttpClient());

        var downloader = new DownloaderService(mockFactory.Object, NullLogger<DownloaderService>.Instance);
        var fabric = new FabricProvider(httpClient, downloader);
        var forge = new ForgeProvider(httpClient, downloader);
        var neoforge = new NeoForgeProvider(httpClient, downloader);
        var manifestService = new AddonManifestService();

        var service = new ModpackService(
            httpClient,
            downloader,
            fabric,
            forge,
            neoforge,
            _instanceManager,
            _parser,
            manifestService,
            _appState,
            NullLogger<ModpackService>.Instance
        );

        var pack = new ModpackImportResult
        {
            Name = "TestPack",
            MinecraftVersion = "1.20.4",
            Loader = "NeoForge",
            LoaderVersion = "20.4.80"
        };
        pack.Mods.Add(new ModpackFile
        {
            Name = "TestMod.jar",
            DownloadUrl = "https://cdn.modrinth.com/data/AABBCC/versions/EEFFGG/TestMod.jar",
            DestinationPath = "mods/TestMod.jar",
            Provider = "Modrinth",
            ProjectId = "AABBCC",
            VersionId = "EEFFGG"
        });

        var metadata = new InstanceMetadata();

        // Create dummy instance metadata file
        string metadataPath = _pathService.GetInstancePath(_tempDir);
        Directory.CreateDirectory(metadataPath);
        _instanceManager.SaveMetadata(metadata, _tempDir);

        // Create empty zip
        string zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create)) { }

        // Act
        await service.ResolveModUrlsAsync(pack);
        ModpackImportResultReport report;
        try
        {
            report = await service.ImportToExistingInstanceAsync(pack, metadata, _tempDir, zipPath, new Progress<PocketMC.Application.Interfaces.Instances.InstanceTransferProgress>());
        }
        catch (Exception ex)
        {
            throw new Exception($"Import failed. Inner exception details: {ex.InnerException?.ToString() ?? "null"}", ex);
        }

        // Assert
        Assert.True(report.Success);
        Assert.Single(report.Mods);
        Assert.True(report.Mods[0].Success, report.Mods[0].ErrorMessage);

        // Verify the loader was downloaded (NeoForge request URL)
        lock (_sentRequests)
        {
            Assert.Contains(_sentRequests, r => r.RequestUri != null && r.RequestUri.AbsoluteUri.Contains("maven.neoforged.net"));
        }

        // Verify registration in addon manifest
        var manifest = await manifestService.LoadManifestAsync(_tempDir);
        Assert.Single(manifest.Entries);
        var entry = manifest.Entries[0];
        Assert.Equal("Modrinth", entry.Provider);
        Assert.Equal("AABBCC", entry.ProjectId);
        Assert.Equal("EEFFGG", entry.VersionId);
        Assert.Equal("TestMod.jar", entry.FileName);
    }

    [Fact]
    public async Task ResolveModUrls_ShouldUseOfficialCurseForgeApiIfKeyIsSet()
    {
        // Arrange
        var httpClient = CreateMockHttpClient();
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => CreateMockHttpClient());

        var downloader = new DownloaderService(mockFactory.Object, NullLogger<DownloaderService>.Instance);
        var fabric = new FabricProvider(httpClient, downloader);
        var forge = new ForgeProvider(httpClient, downloader);
        var neoforge = new NeoForgeProvider(httpClient, downloader);
        var manifestService = new AddonManifestService();

        _appState.ApplySettings(new AppSettings { AppRootPath = _tempDir, CurseForgeApiKey = "my-official-key" });

        var service = new ModpackService(
            httpClient,
            downloader,
            fabric,
            forge,
            neoforge,
            _instanceManager,
            _parser,
            manifestService,
            _appState,
            NullLogger<ModpackService>.Instance
        );

        var pack = new ModpackImportResult();
        pack.Mods.Add(new ModpackFile
        {
            Name = "CurseMod",
            DownloadUrl = "CURSEFORGE:12345:67890",
            DestinationPath = "mods/curse.jar"
        });

        var metadata = new InstanceMetadata();

        // Create dummy instance metadata file
        string metadataPath = _pathService.GetInstancePath(_tempDir);
        Directory.CreateDirectory(metadataPath);
        _instanceManager.SaveMetadata(metadata, _tempDir);

        string zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create)) { }

        // Act
        await service.ResolveModUrlsAsync(pack);
        var report = await service.ImportToExistingInstanceAsync(pack, metadata, _tempDir, zipPath, new Progress<PocketMC.Application.Interfaces.Instances.InstanceTransferProgress>());

        // Assert
        Assert.True(report.Success);
        
        lock (_sentRequests)
        {
            // Verify official CurseForge API request was made with the header
            var cfRequest = _sentRequests.Find(r => r.RequestUri != null && r.RequestUri.AbsoluteUri.Contains("api.curseforge.com"));
            Assert.NotNull(cfRequest);
            Assert.True(cfRequest.Headers.Contains("x-api-key"));
            var keyVal = cfRequest.Headers.GetValues("x-api-key");
            Assert.Contains("my-official-key", keyVal);

            // Verify proxy request was NOT made because the official call succeeded
            Assert.DoesNotContain(_sentRequests, r => r.RequestUri != null && r.RequestUri.AbsoluteUri.Contains("api.curse.tools"));
        }
    }

    [Fact]
    public async Task ResolveModUrls_ShouldFallbackToProxyIfKeyIsNotSet()
    {
        // Arrange
        var httpClient = CreateMockHttpClient();
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => CreateMockHttpClient());

        var downloader = new DownloaderService(mockFactory.Object, NullLogger<DownloaderService>.Instance);
        var fabric = new FabricProvider(httpClient, downloader);
        var forge = new ForgeProvider(httpClient, downloader);
        var neoforge = new NeoForgeProvider(httpClient, downloader);
        var manifestService = new AddonManifestService();

        // No key set in app settings, but preserve AppRootPath
        _appState.ApplySettings(new AppSettings { AppRootPath = _tempDir, CurseForgeApiKey = null });

        var service = new ModpackService(
            httpClient,
            downloader,
            fabric,
            forge,
            neoforge,
            _instanceManager,
            _parser,
            manifestService,
            _appState,
            NullLogger<ModpackService>.Instance
        );

        var pack = new ModpackImportResult();
        pack.Mods.Add(new ModpackFile
        {
            Name = "CurseMod",
            DownloadUrl = "CURSEFORGE:12345:67890",
            DestinationPath = "mods/curse.jar"
        });

        var metadata = new InstanceMetadata();

        // Create dummy instance metadata file
        string metadataPath = _pathService.GetInstancePath(_tempDir);
        Directory.CreateDirectory(metadataPath);
        _instanceManager.SaveMetadata(metadata, _tempDir);

        string zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create)) { }

        // Act
        await service.ResolveModUrlsAsync(pack);
        var report = await service.ImportToExistingInstanceAsync(pack, metadata, _tempDir, zipPath, new Progress<PocketMC.Application.Interfaces.Instances.InstanceTransferProgress>());

        // Assert
        Assert.True(report.Success);

        lock (_sentRequests)
        {
            // Verify official request was NOT made
            Assert.DoesNotContain(_sentRequests, r => r.RequestUri != null && r.RequestUri.AbsoluteUri.Contains("api.curseforge.com"));

            // Verify proxy request WAS made
            Assert.Contains(_sentRequests, r => r.RequestUri != null && r.RequestUri.AbsoluteUri.Contains("api.curse.tools"));
        }
    }
}
