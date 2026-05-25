using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests
{
    public class JavaModMetadataServiceTests : IDisposable
    {
        private readonly string _tempPath;

        public JavaModMetadataServiceTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "PocketMC_ModMetadataTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempPath, true); } catch { }
        }

        private string CreateTempJar(string fileName, Action<ZipArchive> buildArchive)
        {
            string fullPath = Path.Combine(_tempPath, fileName);
            using (var fileStream = new FileStream(fullPath, FileMode.Create))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                buildArchive(archive);
            }
            return fullPath;
        }

        [Fact]
        public void ScanJar_FabricMod_ReadsCorrectMetadata()
        {
            string json = @"{
                ""schemaVersion"": 1,
                ""id"": ""testmod"",
                ""version"": ""1.2.3"",
                ""name"": ""Test Mod"",
                ""description"": ""A test Fabric mod"",
                ""environment"": ""*"",
                ""icon"": ""assets/testmod/icon.png"",
                ""depends"": {
                    ""minecraft"": "">=1.16"",
                    ""fabricloader"": "">=0.14""
                }
            }";

            byte[] iconBytes = new byte[100];
            new Random().NextBytes(iconBytes);

            string jarPath = CreateTempJar("fabric-mod.jar", archive =>
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }

                var iconEntry = archive.CreateEntry("assets/testmod/icon.png");
                using (var stream = iconEntry.Open())
                {
                    stream.Write(iconBytes, 0, iconBytes.Length);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("testmod", metadata.ModId);
            Assert.Equal("Test Mod", metadata.DisplayName);
            Assert.Equal("1.2.3", metadata.Version);
            Assert.Equal("A test Fabric mod", metadata.Description);
            Assert.Equal("Fabric", metadata.LoaderType);
            Assert.False(metadata.IsClientOnly);
            Assert.Equal(iconBytes, metadata.IconBytes);
            Assert.Contains("minecraft", metadata.Dependencies);
            Assert.Contains("fabricloader", metadata.Dependencies);
        }

        [Fact]
        public void ScanJar_FabricIconObject_ChoosesLargestIcon()
        {
            string json = @"{
                ""id"": ""testmod"",
                ""name"": ""Test Mod"",
                ""icon"": {
                    ""16"": ""assets/testmod/icon16.png"",
                    ""128"": ""assets/testmod/icon128.png"",
                    ""32"": ""assets/testmod/icon32.png"",
                    ""64"": ""assets/testmod/icon64.png""
                }
            }";

            byte[] iconBytes128 = new byte[100];
            new Random().NextBytes(iconBytes128);

            string jarPath = CreateTempJar("fabric-icon-object.jar", archive =>
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }

                var iconEntry = archive.CreateEntry("assets/testmod/icon128.png");
                using (var stream = iconEntry.Open())
                {
                    stream.Write(iconBytes128, 0, iconBytes128.Length);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("assets/testmod/icon128.png", metadata.IconEntryPath);
            Assert.Equal(iconBytes128, metadata.IconBytes);
        }

        [Fact]
        public void ScanJar_FabricClientOnly_SetsIsClientOnly()
        {
            string json = @"{
                ""id"": ""testmod-client"",
                ""name"": ""Client Mod"",
                ""environment"": ""client""
            }";

            string jarPath = CreateTempJar("fabric-client.jar", archive =>
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.True(metadata.IsClientOnly);
            Assert.Contains("client-only", metadata.Warnings[0]);
        }

        [Fact]
        public void ScanJar_QuiltMod_ReadsCorrectMetadata()
        {
            string json = @"{
                ""schema_version"": 1,
                ""quilt_loader"": {
                    ""id"": ""quilt_mod"",
                    ""version"": ""2.0.0"",
                    ""environment"": ""client"",
                    ""depends"": [
                        { ""id"": ""minecraft"", ""versions"": "">=1.19"" },
                        ""quilt_loader""
                    ],
                    ""metadata"": {
                        ""name"": ""Quilt Test"",
                        ""description"": ""A test Quilt mod"",
                        ""icon"": ""icon.png""
                    }
                }
            }";

            byte[] iconBytes = new byte[50];

            string jarPath = CreateTempJar("quilt-mod.jar", archive =>
            {
                var entry = archive.CreateEntry("quilt.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }

                var iconEntry = archive.CreateEntry("icon.png");
                using (var stream = iconEntry.Open())
                {
                    stream.Write(iconBytes, 0, iconBytes.Length);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("quilt_mod", metadata.ModId);
            Assert.Equal("Quilt Test", metadata.DisplayName);
            Assert.Equal("2.0.0", metadata.Version);
            Assert.Equal("Quilt", metadata.LoaderType);
            Assert.True(metadata.IsClientOnly);
            Assert.Equal(iconBytes, metadata.IconBytes);
            Assert.Contains("minecraft", metadata.Dependencies);
            Assert.Contains("quilt_loader", metadata.Dependencies);
        }

        [Fact]
        public void ScanJar_ForgeMod_ReadsCorrectMetadata()
        {
            string toml = @"
modLoader=""javafml""
loaderVersion=""[36,)""
license=""MIT""

[[mods]]
modId=""forgemod""
version=""4.5.6""
displayName=""Forge Mod Test""
description='''
A test Forge mod
'''
logoFile=""logo.png""
clientSideOnly=true

[[dependencies.forgemod]]
    modId=""minecraft""
    mandatory=true
";

            byte[] iconBytes = new byte[80];

            string jarPath = CreateTempJar("forge-mod.jar", archive =>
            {
                var entry = archive.CreateEntry("META-INF/mods.toml");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(toml);
                }

                var iconEntry = archive.CreateEntry("logo.png");
                using (var stream = iconEntry.Open())
                {
                    stream.Write(iconBytes, 0, iconBytes.Length);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("forgemod", metadata.ModId);
            Assert.Equal("Forge Mod Test", metadata.DisplayName);
            Assert.Equal("4.5.6", metadata.Version);
            Assert.Equal("A test Forge mod", metadata.Description);
            Assert.Equal("Forge", metadata.LoaderType);
            Assert.True(metadata.IsClientOnly);
            Assert.Equal(iconBytes, metadata.IconBytes);
            Assert.Contains("minecraft", metadata.Dependencies);
        }

        [Fact]
        public void ScanJar_NeoForgeMod_InfersNeoForge()
        {
            string toml = @"
modLoader=""neoforge""
loaderVersion=""[1,)""

[[mods]]
modId=""neoforgemod""
displayName=""NeoForge Test""
displayTest=""IGNORE_SERVER_VERSION""
";

            string jarPath = CreateTempJar("neoforge-mod.jar", archive =>
            {
                var entry = archive.CreateEntry("META-INF/neoforge.mods.toml");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(toml);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("neoforgemod", metadata.ModId);
            Assert.Equal("NeoForge Test", metadata.DisplayName);
            Assert.Equal("NeoForge", metadata.LoaderType);
            Assert.True(metadata.IsClientOnly);
        }

        [Fact]
        public void ScanJar_OldForgeMcModInfo_ReadsCorrectMetadata()
        {
            string json = @"[
                {
                    ""modid"": ""oldforgemod"",
                    ""name"": ""Old Forge Mod"",
                    ""version"": ""1.7.10"",
                    ""description"": ""Old school Forge mod"",
                    ""logoFile"": ""assets/oldforge/logo.png"",
                    ""dependencies"": [""minecraft"", ""forge""]
                }
            ]";

            byte[] iconBytes = new byte[75];

            string jarPath = CreateTempJar("old-forge-mod.jar", archive =>
            {
                var entry = archive.CreateEntry("mcmod.info");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }

                var iconEntry = archive.CreateEntry("assets/oldforge/logo.png");
                using (var stream = iconEntry.Open())
                {
                    stream.Write(iconBytes, 0, iconBytes.Length);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("oldforgemod", metadata.ModId);
            Assert.Equal("Old Forge Mod", metadata.DisplayName);
            Assert.Equal("1.7.10", metadata.Version);
            Assert.Equal("Forge", metadata.LoaderType);
            Assert.Equal(iconBytes, metadata.IconBytes);
            Assert.Contains("minecraft", metadata.Dependencies);
        }

        [Fact]
        public void ScanJar_BukkitPluginInModsFolder_AddsWarning()
        {
            string yaml = @"
name: MyPlugin
version: 1.0.0
description: A plugin description
";

            string modsDir = Path.Combine(_tempPath, "mods");
            Directory.CreateDirectory(modsDir);
            string jarPath = Path.Combine(modsDir, "plugin.jar");

            using (var fileStream = new FileStream(jarPath, FileMode.Create))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("plugin.yml");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(yaml);
                }
            }

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("MyPlugin", metadata.DisplayName);
            Assert.Equal("1.0.0", metadata.Version);
            Assert.Equal("Plugin", metadata.LoaderType);
            Assert.True(metadata.IsPluginInModsFolder);
            Assert.Single(metadata.Warnings);
            Assert.Contains("Move it to plugins", metadata.Warnings[0]);
        }

        [Fact]
        public void ScanJar_MalformedJar_ReturnsUnknownWithoutThrowing()
        {
            string jarPath = Path.Combine(_tempPath, "bad.jar");
            File.WriteAllText(jarPath, "This is not a zip file!");

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("Unknown", metadata.LoaderType);
            Assert.Single(metadata.Warnings);
        }

        [Fact]
        public void ScanJar_IconPathTraversal_IsRejected()
        {
            string json = @"{
                ""id"": ""traversalmod"",
                ""name"": ""Traversal Mod"",
                ""icon"": ""../../outside.png""
            }";

            string jarPath = CreateTempJar("traversal.jar", archive =>
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("Traversal Mod", metadata.DisplayName);
            Assert.Null(metadata.IconBytes);
        }

        [Fact]
        public void ScanJar_OversizedIcon_IsIgnored()
        {
            string json = @"{
                ""id"": ""oversizedmod"",
                ""name"": ""Oversized Mod"",
                ""icon"": ""large.png""
            }";

            byte[] largeIconBytes = new byte[1024 * 1024 + 100];

            string jarPath = CreateTempJar("oversized.jar", archive =>
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }

                var iconEntry = archive.CreateEntry("large.png");
                using (var stream = iconEntry.Open())
                {
                    stream.Write(largeIconBytes, 0, largeIconBytes.Length);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("Oversized Mod", metadata.DisplayName);
            Assert.Null(metadata.IconBytes);
        }
    }
}
