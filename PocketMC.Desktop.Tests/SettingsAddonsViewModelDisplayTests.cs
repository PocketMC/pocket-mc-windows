using PocketMC.Desktop.Core.Interfaces;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Settings;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace PocketMC.Desktop.Tests
{
    public class SettingsAddonsVMDisplayTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly TestServiceProvider _serviceProvider;
        private readonly FakeDialogService _dialogService;

        public SettingsAddonsVMDisplayTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PocketMC_VMTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);

            _serviceProvider = new TestServiceProvider();
            _dialogService = new FakeDialogService();

            var manifestService = new AddonManifestService();
            var stateStore = new AddonStateStore();
            var lifecycleService = new FakeLifecycleService();
            _serviceProvider.Register<AddonManifestService>(manifestService);
            _serviceProvider.Register<AddonStateStore>(stateStore);
            _serviceProvider.Register<BedrockAddonInstaller>(new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance));

            var updateService = new AddonUpdateService(
                manifestService,
                null!,
                null!,
                null!,
                null!,
                null!
            );
            _serviceProvider.Register<AddonUpdateService>(updateService);
            _serviceProvider.Register<AddonInventoryService>(
                new AddonInventoryService(
                    manifestService,
                    stateStore,
                    lifecycleService,
                    NullLogger<AddonInventoryService>.Instance));
            _serviceProvider.Register<AddonToggleService>(
                new AddonToggleService(
                    stateStore,
                    lifecycleService,
                    NullLogger<AddonToggleService>.Instance,
                    manifestService));
            _serviceProvider.Register<AddonUpdateCheckService>(
                new AddonUpdateCheckService(
                    manifestService,
                    updateService,
                    NullLogger<AddonUpdateCheckService>.Instance));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private void WriteJsonFile(string relativePath, string content)
        {
            string fullPath = Path.Combine(_tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private void CreateDummyJar(string relativePath, string fabricJson)
        {
            string fullPath = Path.Combine(_tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var fs = new FileStream(fullPath, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(fabricJson);
                }
            }
        }

        [Fact]
        public void LoadAddons_SelectsCorrectFallbackNameAndSource()
        {
            // Arrange
            // 1. Mod with local metadata
            string modName = "fabric-mod.jar";
            CreateDummyJar($"mods/{modName}", @"{
                ""id"": ""my-mod"",
                ""name"": ""My Fabric Mod"",
                ""version"": ""1.0.0""
            }");

            // 2. Mod without metadata but in manifest (uses DisplayName / ProjectTitle)
            string manifestModName = "manifest-mod.jar";
            CreateDummyJar($"mods/{manifestModName}", @"{""id"": ""manifest-mod"", ""version"": ""1.0.0""}");

            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry
            {
                Provider = "Modrinth",
                ProjectId = "project-123",
                VersionId = "ver-456",
                FileName = manifestModName,
                ProjectTitle = "Manifest Project Title",
                DisplayName = "Manifest Display Name"
            });
            WriteJsonFile("addon_manifest.json", System.Text.Json.JsonSerializer.Serialize(manifest));

            // 3. Mod with no metadata and not in manifest (uses cleaned filename)
            string manualModName = "manual_mod_v2.jar";
            CreateDummyJar($"mods/{manualModName}", @"{""id"": ""manual_mod_v2"", ""version"": ""1.0.0""}");

            var metadata = new InstanceMetadata
            {
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            };

            var vm = new SettingsAddonsVM(
                metadata,
                _tempDir,
                null!, // ModpackService
                _dialogService,
                null!, // IAppNavigationService
                _serviceProvider,
                () => false, // isRunningCheck
                () => { } // onAddonChanged
            );

            // Act
            vm.LoadAddonsSync();

            // Assert
            Assert.Equal(3, vm.Mods.Count);

            var mod1 = vm.Mods.First(m => m.FileName == modName);
            Assert.Equal("My Fabric Mod", mod1.DisplayName);
            Assert.Equal("Manual", mod1.SourceLabel);

            var mod2 = vm.Mods.First(m => m.FileName == manifestModName);
            Assert.Equal("manifest mod", mod2.DisplayName);
            Assert.Equal("Modrinth", mod2.SourceLabel);

            var mod3 = vm.Mods.First(m => m.FileName == manualModName);
            Assert.Equal("manual mod v2", mod3.DisplayName);
            Assert.Equal("Manual", mod3.SourceLabel);
        }

        [Fact]
        public async Task ToggleModActiveCommand_EnablesAndDisablesMod()
        {
            // Arrange
            string modName = "test-toggle.jar";
            CreateDummyJar($"mods/{modName}", @"{
                ""id"": ""my-toggle-mod"",
                ""name"": ""Toggle Mod"",
                ""version"": ""1.0.0""
            }");

            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry
            {
                Provider = "Modrinth",
                ProjectId = "toggle-123",
                VersionId = "ver-123",
                FileName = modName,
                ProjectTitle = "Toggle Mod",
                DisplayName = "Toggle Mod"
            });
            WriteJsonFile("addon_manifest.json", System.Text.Json.JsonSerializer.Serialize(manifest));

            var metadata = new InstanceMetadata
            {
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            };

            var vm = new SettingsAddonsVM(
                metadata,
                _tempDir,
                null!,
                _dialogService,
                null!,
                _serviceProvider,
                () => false,
                () => { }
            );

            vm.LoadAddonsSync();
            var modItem = vm.Mods.First(m => m.FileName == modName);
            Assert.False(modItem.IsDisabled);

            // Act - Disable
            await vm.ToggleModActiveAsync(modItem.Path);
            vm.LoadAddonsSync();

            // Assert - Disabled
            string disabledPath = Path.Combine(_tempDir, "mods", ".disabled", "test-toggle.jar.disabled-by-pocketmc");
            Assert.True(File.Exists(disabledPath));
            Assert.False(File.Exists(Path.Combine(_tempDir, "mods", "test-toggle.jar")));

            var manifestService = _serviceProvider.GetService(typeof(AddonManifestService)) as AddonManifestService;
            var updatedManifest = await manifestService!.LoadManifestAsync(_tempDir);
            Assert.Equal("test-toggle.jar", updatedManifest.Entries[0].FileName);

            vm.LoadAddonsSync();
            var disabledItem = vm.Mods.First(m => m.FileName == "test-toggle.jar");
            Assert.True(disabledItem.IsDisabled);

            // Act - Re-enable
            await vm.ToggleModActiveAsync(disabledItem.Path);
            vm.LoadAddonsSync();

            // Assert - Re-enabled
            Assert.True(File.Exists(Path.Combine(_tempDir, "mods", "test-toggle.jar")));
            Assert.False(File.Exists(disabledPath));

            updatedManifest = await manifestService.LoadManifestAsync(_tempDir);
            Assert.Equal("test-toggle.jar", updatedManifest.Entries[0].FileName);
        }

        [Fact]
        public async Task ToggleModActiveAsync_WhenServerIsRunning_DoesNotRenameFileAndShowsWarning()
        {
            // Arrange
            string modName = "test-running.jar";
            CreateDummyJar($"mods/{modName}", @"{
                ""id"": ""my-running-mod"",
                ""name"": ""Running Mod"",
                ""version"": ""1.0.0""
            }");

            var metadata = new InstanceMetadata
            {
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            };

            var vm = new SettingsAddonsVM(
                metadata,
                _tempDir,
                null!,
                _dialogService,
                null!,
                _serviceProvider,
                () => true, // isRunningCheck = true
                () => { }
            );

            vm.LoadAddonsSync();
            var modItem = vm.Mods.First(m => m.FileName == modName);

            _dialogService.ShowMessageCalled = false;

            // Act - Try disabling while server is running
            await vm.ToggleModActiveAsync(modItem.Path);

            // Assert
            Assert.True(File.Exists(Path.Combine(_tempDir, "mods", "test-running.jar")));
            Assert.False(File.Exists(Path.Combine(_tempDir, "mods", ".disabled", "test-running.jar.disabled-by-pocketmc")));
            Assert.True(_dialogService.ShowMessageCalled);
            Assert.Equal("Server is Running", _dialogService.LastMessageTitle);
        }

        [Fact]
        public void LoadAddons_MapsSideSupportCorrectlyFromJarAndManifest()
        {
            // 1. Mod with environment server in fabric.mod.json
            string serverMod = "server-mod.jar";
            CreateDummyJar($"mods/{serverMod}", @"{
                ""id"": ""server-mod"",
                ""name"": ""Server Mod"",
                ""version"": ""1.0.0"",
                ""environment"": ""server""
            }");

            // 2. Mod with no environment (default ClientAndServer)
            string hybridMod = "hybrid-mod.jar";
            CreateDummyJar($"mods/{hybridMod}", @"{
                ""id"": ""hybrid-mod"",
                ""name"": ""Hybrid Mod"",
                ""version"": ""1.0.0""
            }");

            // 3. Mod with no side in jar, but side in manifest (from Modrinth metadata)
            string manifestMod = "manifest-mod.jar";
            CreateDummyJar($"mods/{manifestMod}", @"{""id"": ""manifest-mod"", ""version"": ""1.0.0""}");

            var manifest = new AddonManifest();
            manifest.Entries.Add(new AddonManifestEntry
            {
                Provider = "Modrinth",
                ProjectId = "mod-123",
                VersionId = "ver-123",
                FileName = manifestMod,
                ProjectTitle = "Manifest Mod",
                DisplayName = "Manifest Mod",
                ClientSide = "unsupported",
                ServerSide = "required" // client_side unsupported, server_side required => ServerOnly
            });

            // 4. Mod with server_side optional (OptionalOnServer) in manifest
            string optionalMod = "optional-mod.jar";
            CreateDummyJar($"mods/{optionalMod}", @"{""id"": ""optional-mod"", ""version"": ""1.0.0""}");
            manifest.Entries.Add(new AddonManifestEntry
            {
                Provider = "Modrinth",
                ProjectId = "mod-456",
                VersionId = "ver-456",
                FileName = optionalMod,
                ProjectTitle = "Optional Mod",
                DisplayName = "Optional Mod",
                ClientSide = "required",
                ServerSide = "optional" // server_side optional => OptionalOnServer
            });

            WriteJsonFile("addon_manifest.json", System.Text.Json.JsonSerializer.Serialize(manifest));

            var metadata = new InstanceMetadata
            {
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            };

            var vm = new SettingsAddonsVM(
                metadata,
                _tempDir,
                null!,
                _dialogService,
                null!,
                _serviceProvider,
                () => false,
                () => { }
            );

            // Act
            vm.LoadAddonsSync();

            // Assert
            Assert.Equal(4, vm.Mods.Count);

            var item1 = vm.Mods.First(m => m.FileName == serverMod);
            Assert.Equal(ModSideSupport.ServerOnly, item1.SideSupport);
            Assert.False(item1.IsClientOnly);
            Assert.Equal("Server-only", item1.SideLabel);
            Assert.False(item1.ShowSideBadge);

            var item2 = vm.Mods.First(m => m.FileName == hybridMod);
            Assert.Equal(ModSideSupport.ClientAndServer, item2.SideSupport);
            Assert.False(item2.IsClientOnly);
            Assert.Equal("Client + Server", item2.SideLabel);
            Assert.False(item2.ShowSideBadge);

            var item3 = vm.Mods.First(m => m.FileName == manifestMod);
            Assert.Equal(ModSideSupport.ServerOnly, item3.SideSupport);
            Assert.False(item3.IsClientOnly);
            Assert.Equal("Server-only", item3.SideLabel);
            Assert.False(item3.ShowSideBadge);

            var item4 = vm.Mods.First(m => m.FileName == optionalMod);
            Assert.Equal(ModSideSupport.OptionalOnServer, item4.SideSupport);
            Assert.False(item4.IsClientOnly);
            Assert.Equal("Optional on server", item4.SideLabel);
            Assert.False(item4.ShowSideBadge);
        }
    }

    public class TestServiceProvider : IServiceProvider
    {
        private readonly System.Collections.Generic.Dictionary<Type, object> _services = new();

        public void Register<T>(T instance) where T : class
        {
            _services[typeof(T)] = instance;
        }

        public object? GetService(Type serviceType)
        {
            return _services.TryGetValue(serviceType, out var value) ? value : null;
        }
    }

    public class FakeDialogService : IDialogService
    {
        public bool ShowMessageCalled { get; set; }
        public string? LastMessageTitle { get; set; }
        public string? LastMessageContent { get; set; }

        public Task<DialogResult> ShowDialogAsync(string title, string message, DialogType type = DialogType.Information, bool showCancel = false, string? primaryButtonText = null, string? secondaryButtonText = null, string? cancelButtonText = null, string? linkText = null, string? linkUrl = null)
        {
            return Task.FromResult(DialogResult.Ok);
        }

        public void ShowMessage(string title, string message, DialogType type = DialogType.Information)
        {
            ShowMessageCalled = true;
            LastMessageTitle = title;
            LastMessageContent = message;
        }

        public Task<string?> OpenFolderDialogAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult<string?>(null);
        public Task<string?> PromptPasswordAsync(string title, string message) => Task.FromResult<string?>(null);
        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult(new string[0]);
    }

    public class FakeLifecycleService : IServerLifecycleService
    {
        public event Action<Guid, ServerState>? OnInstanceStateChanged { add { } remove { } }
        public event Action<Guid, int>? OnRestartCountdownTick { add { } remove { } }

        public Task StartAsync(InstanceMetadata meta) => Task.CompletedTask;
        public Task StopAsync(Guid instanceId) => Task.CompletedTask;
        public void Kill(Guid instanceId) { }
        public void KillAll() { }
        public bool IsRunning(Guid instanceId) => false;
        public bool IsWaitingToRestart(Guid instanceId) => false;
        public void AbortRestartDelay(Guid instanceId) { }
        public Task RestartAsync(Guid instanceId) => Task.CompletedTask;
        public IServerProcess? GetProcess(Guid instanceId) => null;
        public DateTime? GetSessionStartTime(Guid instanceId) => null;
        public Task ReleaseInstanceAsync(Guid instanceId) => Task.CompletedTask;
    }
}
