using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests;

public sealed class AddonManagementServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.AddonManagement", Guid.NewGuid().ToString("N"));

    public AddonManagementServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task InventoryScansEnabledModsFromModsFolder()
    {
        string serverDir = CreateServerDir();
        CreateFabricJar(serverDir, "mods/example.jar", "example", "Example Mod", "1.0.0");

        IReadOnlyList<AddonInventoryItem> items = await CreateInventoryService().ScanAsync(JavaMetadata(), serverDir);

        AddonInventoryItem item = Assert.Single(items);
        Assert.Equal(AddonKind.Mod, item.Kind);
        Assert.Equal(AddonState.Enabled, item.State);
        Assert.Equal("mods/example.jar", item.RelativePath);
    }

    [Fact]
    public async Task InventoryScansEnabledPluginsFromPluginsFolder()
    {
        string serverDir = CreateServerDir();
        CreatePluginJar(serverDir, "plugins/example.jar", "ExamplePlugin", "1.2.3");

        IReadOnlyList<AddonInventoryItem> items = await CreateInventoryService().ScanAsync(JavaMetadata("Paper"), serverDir);

        AddonInventoryItem item = Assert.Single(items);
        Assert.Equal(AddonKind.Plugin, item.Kind);
        Assert.Equal(AddonState.Enabled, item.State);
        Assert.Equal("plugins/example.jar", item.RelativePath);
        Assert.Equal("ExamplePlugin", item.DisplayName);
    }

    [Fact]
    public async Task InventoryScansDisabledModsFromDisabledFolder()
    {
        string serverDir = CreateServerDir();
        CreateFabricJar(serverDir, "mods/.disabled/example.jar.disabled-by-pocketmc", "example", "Example Mod", "1.0.0");

        IReadOnlyList<AddonInventoryItem> items = await CreateInventoryService().ScanAsync(JavaMetadata(), serverDir);

        AddonInventoryItem item = Assert.Single(items);
        Assert.Equal(AddonKind.Mod, item.Kind);
        Assert.Equal(AddonState.Disabled, item.State);
        Assert.Equal("mods/.disabled/example.jar.disabled-by-pocketmc", item.RelativePath);
        Assert.Equal("example.jar", item.FileName);
        Assert.True(item.CanEnable);
        Assert.False(item.CanDisable);
    }

    [Fact]
    public async Task InventoryScansDisabledPluginsFromDisabledFolder()
    {
        string serverDir = CreateServerDir();
        CreatePluginJar(serverDir, "plugins/.disabled/example.jar.disabled-by-pocketmc", "ExamplePlugin", "1.2.3");

        IReadOnlyList<AddonInventoryItem> items = await CreateInventoryService().ScanAsync(JavaMetadata("Paper"), serverDir);

        AddonInventoryItem item = Assert.Single(items);
        Assert.Equal(AddonKind.Plugin, item.Kind);
        Assert.Equal(AddonState.Disabled, item.State);
        Assert.Equal("plugins/.disabled/example.jar.disabled-by-pocketmc", item.RelativePath);
        Assert.Equal("example.jar", item.FileName);
    }

    [Fact]
    public async Task InventorySurfacesJavaMetadataDisplayVersionIconLoaderAndWarnings()
    {
        string serverDir = CreateServerDir();
        byte[] iconBytes = [1, 2, 3, 4, 5];
        CreateFabricJar(serverDir, "mods/rich.jar", "rich", "Rich Mod", "9.8.7", iconBytes, environment: "client");

        AddonInventoryItem item = Assert.Single(await CreateInventoryService().ScanAsync(JavaMetadata(), serverDir));

        Assert.Equal("Rich Mod", item.DisplayName);
        Assert.Equal("9.8.7", item.Version);
        Assert.Equal("Fabric", item.LoaderType);
        Assert.Equal(iconBytes, item.IconBytes);
        Assert.Equal(ModSideSupport.ClientOnly, item.SideSupport);
        Assert.Equal("Client-only", item.SideLabel);
        Assert.Contains(item.Warnings, warning => warning.Contains("client-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DisableMovesJarToDisabledFolderAndWritesSidecar()
    {
        string serverDir = CreateServerDir();
        CreateFabricJar(serverDir, "mods/example.jar", "example", "Example Mod", "1.0.0");
        var metadata = JavaMetadata();

        AddonToggleResult result = await CreateToggleService().DisableAsync(
            metadata,
            serverDir,
            AddonKind.Mod,
            "mods/example.jar",
            AddonDisabledBySource.User,
            "testing");

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(Path.Combine(serverDir, "mods", "example.jar")));
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", ".disabled", "example.jar.disabled-by-pocketmc")));

        AddonStateDocument state = await new AddonStateStore().LoadAsync(serverDir);
        AddonStateEntry entry = Assert.Single(state.Entries);
        Assert.Equal("mods/example.jar", entry.OriginalRelativePath);
        Assert.Equal("mods/.disabled/example.jar.disabled-by-pocketmc", entry.DisabledRelativePath);
        Assert.Equal(AddonDisabledBySource.User, entry.DisabledBy);
        Assert.Equal("testing", entry.DisabledReason);
    }

    [Fact]
    public async Task EnableMovesDisabledJarBackToOriginalFolder()
    {
        string serverDir = CreateServerDir();
        var metadata = JavaMetadata();
        CreateFabricJar(serverDir, "mods/example.jar", "example", "Example Mod", "1.0.0");
        AddonToggleService service = CreateToggleService();

        AddonToggleResult disabled = await service.DisableAsync(metadata, serverDir, AddonKind.Mod, "mods/example.jar");
        Assert.True(disabled.Success, disabled.Message);

        AddonToggleResult enabled = await service.EnableAsync(
            metadata,
            serverDir,
            AddonKind.Mod,
            "mods/.disabled/example.jar.disabled-by-pocketmc");

        Assert.True(enabled.Success, enabled.Message);
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", "example.jar")));
        Assert.False(File.Exists(Path.Combine(serverDir, "mods", ".disabled", "example.jar.disabled-by-pocketmc")));
    }

    [Fact]
    public async Task DisableDoesNotOverwriteExistingDisabledFileAndCreatesUniqueTarget()
    {
        string serverDir = CreateServerDir();
        CreateFabricJar(serverDir, "mods/example.jar", "example", "Example Mod", "1.0.0");
        CreateFabricJar(serverDir, "mods/.disabled/example.jar.disabled-by-pocketmc", "existing", "Existing Mod", "1.0.0");

        AddonToggleResult result = await CreateToggleService().DisableAsync(JavaMetadata(), serverDir, AddonKind.Mod, "mods/example.jar");

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", ".disabled", "example (1).jar.disabled-by-pocketmc")));
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", ".disabled", "example.jar.disabled-by-pocketmc")));
    }

    [Fact]
    public async Task EnableFailsIfEnabledTargetAlreadyExists()
    {
        string serverDir = CreateServerDir();
        CreateFabricJar(serverDir, "mods/example.jar", "enabled", "Enabled Mod", "1.0.0");
        CreateFabricJar(serverDir, "mods/.disabled/example.jar.disabled-by-pocketmc", "disabled", "Disabled Mod", "1.0.0");

        AddonToggleResult result = await CreateToggleService().EnableAsync(
            JavaMetadata(),
            serverDir,
            AddonKind.Mod,
            "mods/.disabled/example.jar.disabled-by-pocketmc");

        Assert.False(result.Success);
        Assert.Equal(AddonToggleErrorCodes.TargetExists, result.ErrorCode);
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", "example.jar")));
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", ".disabled", "example.jar.disabled-by-pocketmc")));
    }

    [Fact]
    public async Task ToggleFailsWhenServerIsRunning()
    {
        string serverDir = CreateServerDir();
        InstanceMetadata metadata = JavaMetadata();
        CreateFabricJar(serverDir, "mods/example.jar", "example", "Example Mod", "1.0.0");

        AddonToggleResult result = await CreateToggleService(running: true).DisableAsync(
            metadata,
            serverDir,
            AddonKind.Mod,
            "mods/example.jar");

        Assert.False(result.Success);
        Assert.Equal(AddonToggleErrorCodes.ServerRunning, result.ErrorCode);
        Assert.Contains("Stop the server", result.Message);
        Assert.True(File.Exists(Path.Combine(serverDir, "mods", "example.jar")));
    }

    [Fact]
    public async Task InventoryReturnsWarningItemForCorruptJar()
    {
        string serverDir = CreateServerDir();
        string jarPath = Path.Combine(serverDir, "mods", "corrupt.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
        await File.WriteAllTextAsync(jarPath, "not a zip");

        AddonInventoryItem item = Assert.Single(await CreateInventoryService().ScanAsync(JavaMetadata(), serverDir));

        Assert.Equal("corrupt.jar", item.FileName);
        Assert.Equal("Unknown", item.LoaderType);
        Assert.NotEmpty(item.Warnings);
    }

    [Fact]
    public async Task DisableLockedFileReturnsStructuredErrorAndLeavesFileInPlace()
    {
        string serverDir = CreateServerDir();
        CreateFabricJar(serverDir, "mods/locked.jar", "locked", "Locked Mod", "1.0.0");
        string lockedPath = Path.Combine(serverDir, "mods", "locked.jar");

        await using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        AddonToggleResult result = await CreateToggleService().DisableAsync(JavaMetadata(), serverDir, AddonKind.Mod, "mods/locked.jar");

        Assert.False(result.Success);
        Assert.Equal(AddonToggleErrorCodes.FileLocked, result.ErrorCode);
        Assert.True(File.Exists(lockedPath));
    }

    [Fact]
    public async Task ToggleRejectsPathTraversalAttempts()
    {
        string serverDir = CreateServerDir();

        AddonToggleResult result = await CreateToggleService().DisableAsync(
            JavaMetadata(),
            serverDir,
            AddonKind.Mod,
            @"mods/../outside.jar");

        Assert.False(result.Success);
        Assert.Equal(AddonToggleErrorCodes.InvalidPath, result.ErrorCode);
    }

    [Fact]
    public void AddonStateStoreUsesAtomicWriteForSidecarState()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Mods",
            "AddonStateStore.cs"));

        Assert.Contains("FileUtils.AtomicWriteAllTextAsync", source);
        Assert.DoesNotContain("File.WriteAllText", source);
        Assert.DoesNotContain("File.WriteAllTextAsync", source);
    }

    [Fact]
    public async Task UnknownProvenanceReturnsUnknownSourceUpdateStatus()
    {
        string serverDir = CreateServerDir();
        AddonInventoryItem item = InventoryItem(serverDir, AddonKind.Mod, "mods/manual.jar", "Fabric");

        AddonUpdateCheckResultModel result = await CreateUpdateCheckService().CheckAsync(JavaMetadata(), serverDir, item);

        Assert.Equal(AddonUpdateStatus.UnknownSource, result.Status);
    }

    [Fact]
    public async Task ProviderFailureReturnsProviderErrorUpdateStatus()
    {
        string serverDir = CreateServerDir();
        await RegisterMarketplaceInstall(serverDir, "failed.jar", provider: "Modrinth", projectId: "project", versionId: "old");
        AddonInventoryItem item = InventoryItem(serverDir, AddonKind.Mod, "mods/failed.jar", "Fabric");
        var updateService = new RecordingAddonUpdateService
        {
            Result = new AddonUpdateCheckResult { Error = "provider unavailable" }
        };

        AddonUpdateCheckResultModel result = await CreateUpdateCheckService(updateService).CheckAsync(JavaMetadata(), serverDir, item);

        Assert.Equal(AddonUpdateStatus.ProviderError, result.Status);
        Assert.Contains("provider unavailable", result.Message);
    }

    [Fact]
    public async Task UpdateCheckerPassesMinecraftVersionAndLoaderToProvider()
    {
        string serverDir = CreateServerDir();
        await RegisterMarketplaceInstall(serverDir, "known.jar", provider: "Modrinth", projectId: "known", versionId: "old-version");
        AddonInventoryItem item = InventoryItem(serverDir, AddonKind.Mod, "mods/known.jar", "Fabric");
        var updateService = new RecordingAddonUpdateService
        {
            Result = new AddonUpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersionId = "new-version",
                LatestVersionName = "2.0.0",
                LatestFileName = "known-2.0.0.jar",
                LatestDownloadUrl = "https://example.test/known.jar"
            }
        };

        AddonUpdateCheckResultModel result = await CreateUpdateCheckService(updateService).CheckAsync(JavaMetadata(), serverDir, item);

        Assert.Equal(AddonUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.20.4", updateService.LastMinecraftVersion);
        Assert.Equal("fabric", updateService.LastLoader);
        Assert.Equal("new-version", result.UpdateInfo?.LatestVersionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateServerDir()
    {
        string serverDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(serverDir);
        return serverDir;
    }

    private static InstanceMetadata JavaMetadata(string serverType = "Fabric")
    {
        return new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            ServerType = serverType,
            MinecraftVersion = "1.20.4"
        };
    }

    private static AddonInventoryService CreateInventoryService(bool running = false)
    {
        return new AddonInventoryService(
            new AddonManifestService(),
            new AddonStateStore(),
            new FakeLifecycleService(running),
            NullLogger<AddonInventoryService>.Instance);
    }

    private static AddonToggleService CreateToggleService(bool running = false)
    {
        return new AddonToggleService(
            new AddonStateStore(),
            new FakeLifecycleService(running),
            NullLogger<AddonToggleService>.Instance);
    }

    private static AddonUpdateCheckService CreateUpdateCheckService(RecordingAddonUpdateService? updateService = null)
    {
        return new AddonUpdateCheckService(
            new AddonManifestService(),
            updateService ?? new RecordingAddonUpdateService(),
            NullLogger<AddonUpdateCheckService>.Instance);
    }

    private static async Task RegisterMarketplaceInstall(
        string serverDir,
        string fileName,
        string provider,
        string projectId,
        string versionId)
    {
        await new AddonManifestService().RegisterInstallAsync(
            serverDir,
            provider,
            projectId,
            versionId,
            fileName,
            projectTitle: "Known Addon",
            iconUrl: null,
            displayName: "Known Addon");
    }

    private static AddonInventoryItem InventoryItem(string serverDir, AddonKind kind, string relativePath, string loaderType)
    {
        string fullPath = Path.Combine(serverDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return new AddonInventoryItem
        {
            InstanceId = Guid.NewGuid(),
            Kind = kind,
            State = AddonState.Enabled,
            DisplayName = Path.GetFileNameWithoutExtension(relativePath),
            FileName = Path.GetFileName(relativePath),
            RelativePath = relativePath,
            FullPath = fullPath,
            LoaderType = loaderType,
            SideSupport = ModSideSupport.ClientAndServer,
            SideLabel = "Client + Server",
            Dependencies = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            UpdateStatus = AddonUpdateStatus.Unknown,
            CanDisable = true,
            RequiresServerStopped = true
        };
    }

    private static void CreateFabricJar(
        string serverDir,
        string relativePath,
        string modId,
        string displayName,
        string version,
        byte[]? iconBytes = null,
        string environment = "*")
    {
        string fullPath = Path.Combine(serverDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var fileStream = new FileStream(fullPath, FileMode.Create);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        var metadata = archive.CreateEntry("fabric.mod.json");
        using (var writer = new StreamWriter(metadata.Open()))
        {
            string iconJson = iconBytes == null ? "" : @",""icon"": ""assets/example/icon.png""";
            writer.Write(
                $$"""
                {
                  "id": "{{modId}}",
                  "name": "{{displayName}}",
                  "version": "{{version}}",
                  "environment": "{{environment}}"{{iconJson}}
                }
                """);
        }

        if (iconBytes != null)
        {
            var icon = archive.CreateEntry("assets/example/icon.png");
            using var iconStream = icon.Open();
            iconStream.Write(iconBytes, 0, iconBytes.Length);
        }
    }

    private static void CreatePluginJar(string serverDir, string relativePath, string name, string version)
    {
        string fullPath = Path.Combine(serverDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var fileStream = new FileStream(fullPath, FileMode.Create);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        var metadata = archive.CreateEntry("plugin.yml");
        using var writer = new StreamWriter(metadata.Open());
        writer.WriteLine($"name: {name}");
        writer.WriteLine($"version: {version}");
        writer.WriteLine("api-version: '1.20'");
    }

    private sealed class RecordingAddonUpdateService : AddonUpdateService
    {
        public RecordingAddonUpdateService()
            : base(new AddonManifestService(), null!, null!, null!, null!, null)
        {
        }

        public string? LastMinecraftVersion { get; private set; }
        public string? LastLoader { get; private set; }
        public AddonUpdateCheckResult Result { get; set; } = new();

        public override Task<AddonUpdateCheckResult> CheckForUpdateFromEntryAsync(
            AddonManifestEntry entry,
            string mcVersion,
            string loader,
            EngineCompatibility compat)
        {
            LastMinecraftVersion = mcVersion;
            LastLoader = loader;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeLifecycleService : IServerLifecycleService
    {
        private readonly bool _running;

        public FakeLifecycleService(bool running)
        {
            _running = running;
        }

        public event Action<Guid, ServerState>? OnInstanceStateChanged { add { } remove { } }
        public event Action<Guid, int>? OnRestartCountdownTick { add { } remove { } }

        public Task StartAsync(InstanceMetadata meta) => Task.CompletedTask;
        public Task StopAsync(Guid instanceId) => Task.CompletedTask;
        public void Kill(Guid instanceId) { }
        public void KillAll() { }
        public bool IsRunning(Guid instanceId) => _running;
        public bool IsWaitingToRestart(Guid instanceId) => false;
        public void AbortRestartDelay(Guid instanceId) { }
        public Task RestartAsync(Guid instanceId) => Task.CompletedTask;
        public ServerProcess? GetProcess(Guid instanceId) => null;
        public DateTime? GetSessionStartTime(Guid instanceId) => null;
        public Task ReleaseInstanceAsync(Guid instanceId) => Task.CompletedTask;
    }
}



