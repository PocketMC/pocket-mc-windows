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
            Assert.Equal(">=1.16", metadata.RequiredMinecraftVersion);
            Assert.Equal(">=0.14", metadata.RequiredLoaderVersion);
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
            Assert.Equal(">=1.19", metadata.RequiredMinecraftVersion);
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
            Assert.False(metadata.IsClientOnly);
            Assert.Equal(ModSideSupport.OptionalOnServer, metadata.SideSupport);
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


        }

        [Fact]
        public void ScanJar_MalformedJar_ReturnsUnknownWithoutThrowing()
        {
            string jarPath = Path.Combine(_tempPath, "bad.jar");
            File.WriteAllText(jarPath, "This is not a zip file!");

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("Unknown", metadata.LoaderType);

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

        [Fact]
        public void ScanJar_FabricEnvironment_MapsCorrectly()
        {
            // client environment
            string jsonClient = @"{ ""id"": ""c"", ""environment"": ""client"" }";
            string jarClient = CreateTempJar("fabric-env-client.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("fabric.mod.json").Open());
                w.Write(jsonClient);
            });
            var metaClient = JavaModMetadataService.ScanJar(jarClient);
            Assert.Equal(ModSideSupport.ClientOnly, metaClient.SideSupport);
            Assert.True(metaClient.IsClientOnly);


            // server environment
            string jsonServer = @"{ ""id"": ""s"", ""environment"": ""server"" }";
            string jarServer = CreateTempJar("fabric-env-server.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("fabric.mod.json").Open());
                w.Write(jsonServer);
            });
            var metaServer = JavaModMetadataService.ScanJar(jarServer);
            Assert.Equal(ModSideSupport.ServerOnly, metaServer.SideSupport);
            Assert.False(metaServer.IsClientOnly);

            // '*' environment
            string jsonStar = @"{ ""id"": ""star"", ""environment"": ""*"" }";
            string jarStar = CreateTempJar("fabric-env-star.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("fabric.mod.json").Open());
                w.Write(jsonStar);
            });
            var metaStar = JavaModMetadataService.ScanJar(jarStar);
            Assert.Equal(ModSideSupport.ClientAndServer, metaStar.SideSupport);

            // missing environment
            string jsonMissing = @"{ ""id"": ""m"" }";
            string jarMissing = CreateTempJar("fabric-env-missing.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("fabric.mod.json").Open());
                w.Write(jsonMissing);
            });
            var metaMissing = JavaModMetadataService.ScanJar(jarMissing);
            Assert.Equal(ModSideSupport.ClientAndServer, metaMissing.SideSupport);
        }

        [Fact]
        public void ScanJar_QuiltEnvironment_MapsCorrectly()
        {
            // Quilt client environment
            string jsonClient = @"{ ""quilt_loader"": { ""id"": ""qc"", ""environment"": ""client"", ""metadata"": { ""name"": ""QClient"" } } }";
            string jarClient = CreateTempJar("quilt-env-client.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("quilt.mod.json").Open());
                w.Write(jsonClient);
            });
            var metaClient = JavaModMetadataService.ScanJar(jarClient);
            Assert.Equal(ModSideSupport.ClientOnly, metaClient.SideSupport);
            Assert.True(metaClient.IsClientOnly);

            // Quilt server environment
            string jsonServer = @"{ ""quilt_loader"": { ""id"": ""qs"", ""environment"": ""server"", ""metadata"": { ""name"": ""QServer"" } } }";
            string jarServer = CreateTempJar("quilt-env-server.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("quilt.mod.json").Open());
                w.Write(jsonServer);
            });
            var metaServer = JavaModMetadataService.ScanJar(jarServer);
            Assert.Equal(ModSideSupport.ServerOnly, metaServer.SideSupport);
            Assert.False(metaServer.IsClientOnly);

            // Quilt missing environment
            string jsonMissing = @"{ ""quilt_loader"": { ""id"": ""qm"", ""metadata"": { ""name"": ""QMissing"" } } }";
            string jarMissing = CreateTempJar("quilt-env-missing.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("quilt.mod.json").Open());
                w.Write(jsonMissing);
            });
            var metaMissing = JavaModMetadataService.ScanJar(jarMissing);
            Assert.Equal(ModSideSupport.ClientAndServer, metaMissing.SideSupport);
        }

        [Fact]
        public void ScanJar_ForgeDisplayTestAndClientSideOnly_MapsCorrectly()
        {
            // displayTest = IGNORE_SERVER_VERSION
            string toml1 = @"[[mods]]
modId=""test1""
displayTest=""IGNORE_SERVER_VERSION""";
            string jar1 = CreateTempJar("forge-display-test-1.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml1);
            });
            var meta1 = JavaModMetadataService.ScanJar(jar1);
            Assert.Equal(ModSideSupport.OptionalOnServer, meta1.SideSupport);
            Assert.False(meta1.IsClientOnly);

            // displayTest = NONE
            string toml2 = @"[[mods]]
modId=""test2""
displayTest=""NONE""";
            string jar2 = CreateTempJar("forge-display-test-2.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml2);
            });
            var meta2 = JavaModMetadataService.ScanJar(jar2);
            Assert.Equal(ModSideSupport.OptionalOnServer, meta2.SideSupport);
            Assert.False(meta2.IsClientOnly);

            // clientSideOnly = true
            string toml3 = @"[[mods]]
modId=""test3""
clientSideOnly=true";
            string jar3 = CreateTempJar("forge-clientsideonly.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml3);
            });
            var meta3 = JavaModMetadataService.ScanJar(jar3);
            Assert.Equal(ModSideSupport.ClientOnly, meta3.SideSupport);
            Assert.True(meta3.IsClientOnly);

            // No side info
            string toml4 = @"[[mods]]
modId=""test4""";
            string jar4 = CreateTempJar("forge-noside.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml4);
            });
            var meta4 = JavaModMetadataService.ScanJar(jar4);
            Assert.Equal(ModSideSupport.Unknown, meta4.SideSupport);
            Assert.False(meta4.IsClientOnly);
        }

        [Fact]
        public void ScanJar_ForgeClientSideOnlyInDescription_DoesNotFlagAsClientOnly()
        {
            // clientSideOnly=true appears ONLY inside a triple-quoted description.
            // This must NOT mark the mod as client-only.
            string toml = @"
modLoader=""javafml""
loaderVersion=""[36,)""

[[mods]]
modId=""servermod""
displayName=""Server Mod""
version=""1.0.0""
description='''
This is a server-side mod. Note: if you see clientSideOnly=true in old docs,
that refers to legacy Forge config and does not apply here.
'''
";

            string jarPath = CreateTempJar("forge-desc-false-positive.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml);
            });
            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("servermod", metadata.ModId);
            Assert.False(metadata.IsClientOnly, "clientSideOnly inside a description should not flag the mod.");
            Assert.Equal(ModSideSupport.Unknown, metadata.SideSupport);
        }

        [Fact]
        public void ScanJar_ForgeClientSideOnlyInComment_DoesNotFlagAsClientOnly()
        {
            // clientSideOnly=true appears ONLY in a # comment line.
            string toml = @"
modLoader=""javafml""
loaderVersion=""[36,)""

[[mods]]
modId=""servermod2""
displayName=""Server Mod 2""
version=""2.0.0""
# clientSideOnly=true  -- this is commented out
";

            string jarPath = CreateTempJar("forge-comment-false-positive.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml);
            });
            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("servermod2", metadata.ModId);
            Assert.False(metadata.IsClientOnly, "clientSideOnly in a comment should not flag the mod.");
            Assert.Equal(ModSideSupport.Unknown, metadata.SideSupport);
        }

        [Fact]
        public void ScanJar_ForgeClientSideOnlyInQuotedValue_DoesNotFlagAsClientOnly()
        {
            // clientSideOnly=true appears inside a regular quoted string value (e.g. as part of a description).
            string toml = @"
modLoader=""javafml""
loaderVersion=""[36,)""

[[mods]]
modId=""servermod3""
displayName=""Server Mod 3""
version=""3.0.0""
description=""Set clientSideOnly=true in your client config to enable overlays.""
";

            string jarPath = CreateTempJar("forge-quoted-false-positive.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml);
            });
            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("servermod3", metadata.ModId);
            Assert.False(metadata.IsClientOnly, "clientSideOnly inside a quoted string value should not flag the mod.");
            Assert.Equal(ModSideSupport.Unknown, metadata.SideSupport);
        }

        [Fact]
        public void ScanJar_ForgeClientSideOnlyInInlineComment_DoesNotFlagAsClientOnly()
        {
            // clientSideOnly=true appears in an inline comment after a real key=value.
            string toml = @"
modLoader=""javafml""
loaderVersion=""[36,)""

[[mods]]
modId=""servermod4""
displayName=""Server Mod 4""
version=""4.0.0""
someOtherKey=""value"" # clientSideOnly=true was removed in v2
";

            string jarPath = CreateTempJar("forge-inline-comment-false-positive.jar", a =>
            {
                using var w = new StreamWriter(a.CreateEntry("META-INF/mods.toml").Open());
                w.Write(toml);
            });
            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("servermod4", metadata.ModId);
            Assert.False(metadata.IsClientOnly, "clientSideOnly in an inline comment should not flag the mod.");
            Assert.Equal(ModSideSupport.Unknown, metadata.SideSupport);
        }

        [Fact]
        public void ScanJar_HybridFabricQuiltMod_ShouldClassifyAsQuilt()
        {
            string quiltJson = "{\"quilt_loader\": {\"id\": \"quiltmod\", \"version\": \"1.0.0\"}}";
            string fabricJson = "{\"id\": \"fabricmod\", \"version\": \"1.0.0\"}";

            string jarPath = CreateTempJar("hybrid-mod.jar", a =>
            {
                using (var w = new StreamWriter(a.CreateEntry("quilt.mod.json").Open()))
                {
                    w.Write(quiltJson);
                }
                using (var w = new StreamWriter(a.CreateEntry("fabric.mod.json").Open()))
                {
                    w.Write(fabricJson);
                }
            });

            var metadata = JavaModMetadataService.ScanJar(jarPath);

            Assert.Equal("Quilt", metadata.LoaderType);
            Assert.Equal("quiltmod", metadata.ModId);
        }
    }
}


