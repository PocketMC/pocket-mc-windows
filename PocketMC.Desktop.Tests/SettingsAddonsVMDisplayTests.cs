using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Models;
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
            _serviceProvider.Register<AddonManifestService>(manifestService);
            _serviceProvider.Register<BedrockAddonInstaller>(new BedrockAddonInstaller(NullLogger<BedrockAddonInstaller>.Instance));

            var updateService = new AddonUpdateService(
                manifestService,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!
            );
            _serviceProvider.Register<AddonUpdateService>(updateService);
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
            File.WriteAllText(Path.Combine(_tempDir, "mods", manifestModName), "dummy jar content");

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
            File.WriteAllText(Path.Combine(_tempDir, "mods", manualModName), "dummy jar content");

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
                () => {} // onAddonChanged
            );

            // Act
            vm.LoadAddonsSync();

            // Assert
            Assert.Equal(3, vm.Mods.Count);

            var mod1 = vm.Mods.First(m => m.FileName == modName);
            Assert.Equal("My Fabric Mod", mod1.DisplayName);
            Assert.Equal("Manual", mod1.SourceLabel);

            var mod2 = vm.Mods.First(m => m.FileName == manifestModName);
            Assert.Equal("Manifest Display Name", mod2.DisplayName);
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
                () => {}
            );

            vm.LoadAddonsSync();
            var modItem = vm.Mods.First(m => m.FileName == modName);
            Assert.False(modItem.IsDisabled);

            // Act - Disable
            await vm.ToggleModActiveAsync(modItem.Path);
            vm.LoadAddonsSync();

            // Assert - Disabled
            string disabledPath = Path.Combine(_tempDir, "mods", "test-toggle.jar.disabled");
            Assert.True(File.Exists(disabledPath));
            Assert.False(File.Exists(Path.Combine(_tempDir, "mods", "test-toggle.jar")));

            var manifestService = _serviceProvider.GetService(typeof(AddonManifestService)) as AddonManifestService;
            var updatedManifest = await manifestService!.LoadManifestAsync(_tempDir);
            Assert.Equal("test-toggle.jar.disabled", updatedManifest.Entries[0].FileName);

            vm.LoadAddonsSync();
            var disabledItem = vm.Mods.First(m => m.FileName == "test-toggle.jar.disabled");
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
                () => {}
            );

            vm.LoadAddonsSync();
            var modItem = vm.Mods.First(m => m.FileName == modName);
            
            _dialogService.ShowMessageCalled = false;

            // Act - Try disabling while server is running
            await vm.ToggleModActiveAsync(modItem.Path);

            // Assert
            Assert.True(File.Exists(Path.Combine(_tempDir, "mods", "test-running.jar")));
            Assert.False(File.Exists(Path.Combine(_tempDir, "mods", "test-running.jar.disabled")));
            Assert.True(_dialogService.ShowMessageCalled);
            Assert.Equal("Server is Running", _dialogService.LastMessageTitle);
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

        public Task<DialogResult> ShowDialogAsync(string title, string message, DialogType type = DialogType.Information, bool showCancel = false, string? primaryButtonText = null, string? secondaryButtonText = null, string? cancelButtonText = null)
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
        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult(new string[0]);
    }
}
