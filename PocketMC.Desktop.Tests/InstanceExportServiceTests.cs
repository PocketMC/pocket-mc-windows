using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC_Export_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_JavaInstance_PackagesJarFilesAndCreatesCanonicalManifest()
    {
        string instanceRoot = Path.Combine(_root, "paper-instance");
        Directory.CreateDirectory(instanceRoot);
        WriteFile(instanceRoot, ".pocket-mc.json", "{\"name\":\"Paper Test\"}");
        WriteFile(instanceRoot, "server.properties", "motd=Paper Test");
        WriteFile(instanceRoot, "whitelist.json", "[]");
        WriteFile(instanceRoot, "server.jar", "server binary");
        WriteFile(instanceRoot, "plugins\\Essentials.jar", "plugin binary");
        WriteFile(instanceRoot, "plugins\\Essentials\\config.yml", "enabled: true");
        WriteFile(instanceRoot, "mods\\LocalOnly.jar", "local mod binary");
        WriteFile(instanceRoot, "config\\paper-global.yml", "settings: {}");
        WriteFile(instanceRoot, "world\\level.dat", "world");
        WriteFile(instanceRoot, "world\\session.lock", "lock");

        await new AddonManifestService().SaveManifestAsync(instanceRoot, new AddonManifest
        {
            Entries =
            {
                new AddonManifestEntry
                {
                    Provider = "Modrinth",
                    ProjectId = "essentials",
                    VersionId = "1.3",
                    FileName = "Essentials.jar",
                    ProjectTitle = "Essentials",
                    FileHash = "abcdef",
                    FileHashType = "sha512",
                    Loader = "paper"
                }
            }
        });

        string exportPath = Path.Combine(_root, "paper-export.zip");
        var service = CreateService();

        InstanceExportResult result = await service.ExportAsync(new InstanceExportRequest
        {
            Metadata = new InstanceMetadata
            {
                Name = "Paper Test",
                Description = "Java export",
                ServerType = "Paper",
                MinecraftVersion = "1.20.4"
            },
            InstancePath = instanceRoot,
            DestinationZipPath = exportPath
        });

        using ZipArchive archive = ZipFile.OpenRead(exportPath);
        AssertEntryExists(archive, "manifest.json");
        AssertEntryExists(archive, "pocket-mc.json");
        AssertEntryExists(archive, "server/server.properties");
        AssertEntryExists(archive, "server/plugins/Essentials/config.yml");
        AssertEntryExists(archive, "server/config/paper-global.yml");
        AssertEntryExists(archive, "server/world/level.dat");
        // JAR files are now PACKAGED in the ZIP for self-contained exports
        AssertEntryExists(archive, "server/plugins/Essentials.jar");
        AssertEntryExists(archive, "server/mods/LocalOnly.jar");
        // server.jar at root should still be missing because only specific root files and directories are enumerated
        AssertEntryMissing(archive, "server/server.jar");

        Assert.Contains("world/session.lock", result.SkippedFiles);

        InstanceExportManifest manifest = ReadManifest(archive);
        Assert.Equal(InstanceServerPlatform.Java, manifest.Software.Platform);
        Assert.Equal("Paper", manifest.Software.Type);
        Assert.Equal(InstanceRuntimeType.Java, manifest.Runtime.Type);
        Assert.Equal("17", manifest.Runtime.TargetVersion);

        // Verify canonical addon entries
        JavaAddonManifest remoteAddon = Assert.IsType<JavaAddonManifest>(
            manifest.Addons.Single(addon => addon.Name == "Essentials"));
        Assert.Equal(InstanceAddonTypes.Plugin, remoteAddon.Type);
        Assert.Equal("Modrinth", remoteAddon.Provider);
        Assert.Equal("essentials", remoteAddon.ProjectId);
        Assert.Equal("sha512-abcdef", remoteAddon.Hash);
        Assert.False(remoteAddon.IsDisabled);
        Assert.NotNull(remoteAddon.Sha1);
        Assert.NotNull(remoteAddon.Sha512);
        Assert.True(remoteAddon.Size > 0);
        Assert.NotNull(remoteAddon.PackagedPath);

        JavaAddonManifest localAddon = Assert.IsType<JavaAddonManifest>(
            manifest.Addons.Single(addon => addon.Name == "LocalOnly"));
        Assert.Equal(InstanceAddonTypes.Mod, localAddon.Type);
        Assert.Equal("Local", localAddon.Provider);
        Assert.False(localAddon.IsDisabled);
        Assert.NotNull(localAddon.Sha1);
        Assert.NotNull(localAddon.Sha512);
    }

    [Fact]
    public async Task ExportAsync_DisabledAddon_SetsIsDisabledFlagAndPackagesFile()
    {
        string instanceRoot = Path.Combine(_root, "disabled-instance");
        Directory.CreateDirectory(instanceRoot);
        WriteFile(instanceRoot, ".pocket-mc.json", "{\"name\":\"Disabled Test\"}");
        WriteFile(instanceRoot, "server.properties", "motd=Disabled Test");
        WriteFile(instanceRoot, "mods\\AudioPlayer.jar" + AddonFileNamePolicy.DisabledSuffix, "disabled mod binary");

        string exportPath = Path.Combine(_root, "disabled-export.zip");
        var service = CreateService();

        await service.ExportAsync(new InstanceExportRequest
        {
            Metadata = new InstanceMetadata
            {
                Name = "Disabled Test",
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            },
            InstancePath = instanceRoot,
            DestinationZipPath = exportPath
        });

        using ZipArchive archive = ZipFile.OpenRead(exportPath);
        // Disabled JAR should be packaged with its original filename (including disabled suffix)
        AssertEntryExists(archive, "server/mods/AudioPlayer.jar" + AddonFileNamePolicy.DisabledSuffix);

        InstanceExportManifest manifest = ReadManifest(archive);
        JavaAddonManifest addon = Assert.IsType<JavaAddonManifest>(
            manifest.Addons.Single(a => a.Name == "AudioPlayer"));
        Assert.True(addon.IsDisabled);
        Assert.Equal("AudioPlayer.jar", addon.FileName); // Enabled file name in manifest
        Assert.NotNull(addon.PackagedPath);
        Assert.NotNull(addon.Sha1);
        Assert.NotNull(addon.Sha512);
        Assert.True(addon.Size > 0);
    }

    [Fact]
    public async Task ExportAsync_DuplicateProviderEntries_MergedIntoProviderIdentities()
    {
        string instanceRoot = Path.Combine(_root, "duplicate-instance");
        Directory.CreateDirectory(instanceRoot);
        WriteFile(instanceRoot, ".pocket-mc.json", "{\"name\":\"Dup Test\"}");
        WriteFile(instanceRoot, "server.properties", "motd=Dup Test");
        WriteFile(instanceRoot, "mods\\cloth-config.jar", "mod binary");

        // Simulate duplicate entries — one CurseForge, one Modrinth — for the same physical file
        await new AddonManifestService().SaveManifestAsync(instanceRoot, new AddonManifest
        {
            Entries =
            {
                new AddonManifestEntry
                {
                    Provider = "Modrinth",
                    ProjectId = "modrinth-cloth",
                    VersionId = "v1",
                    FileName = "cloth-config.jar",
                    DisplayName = "Cloth Config API"
                },
                new AddonManifestEntry
                {
                    Provider = "CurseForge",
                    ProjectId = "curseforge-cloth",
                    VersionId = "v2",
                    FileName = "cloth-config.jar",
                    DisplayName = "Cloth Config API"
                }
            }
        });

        string exportPath = Path.Combine(_root, "dup-export.zip");
        var service = CreateService();

        await service.ExportAsync(new InstanceExportRequest
        {
            Metadata = new InstanceMetadata
            {
                Name = "Dup Test",
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            },
            InstancePath = instanceRoot,
            DestinationZipPath = exportPath
        });

        InstanceExportManifest manifest = ReadManifest(ZipFile.OpenRead(exportPath));

        // Should produce exactly ONE entry for cloth-config.jar (not two)
        Assert.Single(manifest.Addons.Where(a => a.FileName == "cloth-config.jar"));

        JavaAddonManifest addon = Assert.IsType<JavaAddonManifest>(
            manifest.Addons.Single(a => a.FileName == "cloth-config.jar"));

        // Should have ProviderIdentities with both Modrinth and CurseForge
        Assert.NotNull(addon.ProviderIdentities);
        Assert.Equal(2, addon.ProviderIdentities.Count);
        Assert.Contains(addon.ProviderIdentities, pi => pi.Provider == "Modrinth" && pi.ProjectId == "modrinth-cloth");
        Assert.Contains(addon.ProviderIdentities, pi => pi.Provider == "CurseForge" && pi.ProjectId == "curseforge-cloth");
    }

    [Fact]
    public async Task ExportAsync_ScrubsBackupStateFromPortableMetadataAndPackage()
    {
        string instanceRoot = Path.Combine(_root, "backup-state-instance");
        string customBackupDirectory = Path.Combine(_root, "original-custom-backups");
        Directory.CreateDirectory(instanceRoot);
        Directory.CreateDirectory(customBackupDirectory);
        WriteFile(instanceRoot, "server.properties", "motd=Backup State Test");
        WriteFile(instanceRoot, "world\\level.dat", "world");
        WriteFile(instanceRoot, "backups\\world-2026-05-29-13-17-37.zip", "backup");
        WriteFile(instanceRoot, "backups\\backup-manifest.json", "{}");

        var sourceMetadata = new InstanceMetadata
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Backup State Test",
            ServerType = "Paper",
            MinecraftVersion = "1.20.4",
            BackupIntervalHours = 6,
            MaxBackupsToKeep = 4,
            LastBackupTime = new DateTime(2026, 5, 29, 8, 0, 0, DateTimeKind.Utc),
            CustomBackupDirectory = customBackupDirectory
        };
        WriteFile(instanceRoot, ".pocket-mc.json", JsonSerializer.Serialize(sourceMetadata));

        string exportPath = Path.Combine(_root, "backup-state-export.zip");
        var service = CreateService();

        await service.ExportAsync(new InstanceExportRequest
        {
            Metadata = sourceMetadata,
            InstancePath = instanceRoot,
            DestinationZipPath = exportPath
        });

        using ZipArchive archive = ZipFile.OpenRead(exportPath);
        AssertEntryMissing(archive, "server/backups/world-2026-05-29-13-17-37.zip");
        AssertEntryMissing(archive, "server/backups/backup-manifest.json");

        InstanceMetadata exportedMetadata = ReadMetadata(archive);
        Assert.Equal(Guid.Empty, exportedMetadata.Id);
        Assert.Null(exportedMetadata.LastBackupTime);
        Assert.Null(exportedMetadata.CustomBackupDirectory);
        Assert.Equal(6, exportedMetadata.BackupIntervalHours);
        Assert.Equal(4, exportedMetadata.MaxBackupsToKeep);
    }

    [Fact]
    public async Task ExportAsync_BedrockInstance_SkipsNativeBinariesAndKeepsPacksAndWorlds()
    {
        string instanceRoot = Path.Combine(_root, "bedrock-instance");
        Directory.CreateDirectory(instanceRoot);
        WriteFile(instanceRoot, ".pocket-mc.json", "{\"name\":\"Bedrock Test\"}");
        WriteFile(instanceRoot, "server.properties", "level-name=Bedrock level");
        WriteFile(instanceRoot, "allowlist.json", "[]");
        WriteFile(instanceRoot, "permissions.json", "[]");
        WriteFile(instanceRoot, "valid_known_packs.json", "[]");
        WriteFile(instanceRoot, "bedrock_server.exe", "bds");
        WriteFile(instanceRoot, "bedrock_server.dll", "bds");
        WriteFile(instanceRoot, "behavior_packs\\SparkPortals\\manifest.json", """
            {
              "header": {
                "name": "SparkPortals",
                "uuid": "11111111-2222-3333-4444-555555555555",
                "version": [1, 0, 0]
              }
            }
            """);
        WriteFile(instanceRoot, "behavior_packs\\SparkPortals\\native.dll", "native");
        WriteFile(instanceRoot, "resource_packs\\CleanTextures\\manifest.json", """
            {
              "header": {
                "name": "CleanTextures",
                "uuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                "version": [2, 1, 0]
              }
            }
            """);
        WriteFile(instanceRoot, "worlds\\Bedrock level\\level.dat", "world");

        string exportPath = Path.Combine(_root, "bedrock-export.zip");
        var service = CreateService();

        await service.ExportAsync(new InstanceExportRequest
        {
            Metadata = new InstanceMetadata
            {
                Name = "Bedrock Test",
                Description = "Bedrock export",
                ServerType = "Bedrock",
                MinecraftVersion = "1.20.80"
            },
            InstancePath = instanceRoot,
            DestinationZipPath = exportPath
        });

        using ZipArchive archive = ZipFile.OpenRead(exportPath);
        AssertEntryExists(archive, "server/server.properties");
        AssertEntryExists(archive, "server/allowlist.json");
        AssertEntryExists(archive, "server/permissions.json");
        AssertEntryExists(archive, "server/valid_known_packs.json");
        AssertEntryExists(archive, "server/behavior_packs/SparkPortals/manifest.json");
        AssertEntryExists(archive, "server/resource_packs/CleanTextures/manifest.json");
        AssertEntryExists(archive, "server/worlds/Bedrock level/level.dat");
        AssertEntryMissing(archive, "server/bedrock_server.exe");
        AssertEntryMissing(archive, "server/bedrock_server.dll");
        AssertEntryMissing(archive, "server/behavior_packs/SparkPortals/native.dll");

        InstanceExportManifest manifest = ReadManifest(archive);
        Assert.Equal(InstanceServerPlatform.Bedrock, manifest.Software.Platform);
        Assert.Equal("BDS", manifest.Software.Type);
        Assert.Equal(InstanceRuntimeType.Native, manifest.Runtime.Type);
        Assert.Null(manifest.Runtime.TargetVersion);

        BedrockAddonManifest behaviorPack = Assert.IsType<BedrockAddonManifest>(
            manifest.Addons.Single(addon => addon.Name == "SparkPortals"));
        Assert.Equal(InstanceAddonTypes.BehaviorPack, behaviorPack.Type);
        Assert.Equal("11111111-2222-3333-4444-555555555555", behaviorPack.Uuid);
        Assert.Equal("1.0.0", behaviorPack.Version);

        BedrockAddonManifest resourcePack = Assert.IsType<BedrockAddonManifest>(
            manifest.Addons.Single(addon => addon.Name == "CleanTextures"));
        Assert.Equal(InstanceAddonTypes.ResourcePack, resourcePack.Type);
        Assert.Equal("2.1.0", resourcePack.Version);
    }

    [Fact]
    public async Task ExportAsync_ManifestJsonRoundTrips_WithCanonicalFields()
    {
        // Verify that the new fields survive serialization → deserialization
        string instanceRoot = Path.Combine(_root, "roundtrip-instance");
        Directory.CreateDirectory(instanceRoot);
        WriteFile(instanceRoot, ".pocket-mc.json", "{\"name\":\"Roundtrip Test\"}");
        WriteFile(instanceRoot, "server.properties", "motd=Roundtrip");
        WriteFile(instanceRoot, "mods\\TestMod.jar", "test mod content for hashing");

        await new AddonManifestService().SaveManifestAsync(instanceRoot, new AddonManifest
        {
            Entries =
            {
                new AddonManifestEntry
                {
                    Provider = "Modrinth",
                    ProjectId = "test-mod",
                    VersionId = "v1",
                    FileName = "TestMod.jar",
                    DisplayName = "Test Mod"
                }
            }
        });

        string exportPath = Path.Combine(_root, "roundtrip-export.zip");
        var service = CreateService();

        await service.ExportAsync(new InstanceExportRequest
        {
            Metadata = new InstanceMetadata
            {
                Name = "Roundtrip Test",
                ServerType = "Fabric",
                MinecraftVersion = "1.20.4"
            },
            InstancePath = instanceRoot,
            DestinationZipPath = exportPath
        });

        // Read the manifest, serialize it again, then deserialize once more
        InstanceExportManifest manifest;
        using (ZipArchive archive = ZipFile.OpenRead(exportPath))
        {
            manifest = ReadManifest(archive);
        }

        string json = JsonSerializer.Serialize(manifest, InstanceExportManifest.CreateJsonOptions());
        InstanceExportManifest roundTripped = JsonSerializer.Deserialize<InstanceExportManifest>(json, InstanceExportManifest.CreateJsonOptions())!;

        JavaAddonManifest original = Assert.IsType<JavaAddonManifest>(manifest.Addons[0]);
        JavaAddonManifest restored = Assert.IsType<JavaAddonManifest>(roundTripped.Addons[0]);

        Assert.Equal(original.Sha1, restored.Sha1);
        Assert.Equal(original.Sha512, restored.Sha512);
        Assert.Equal(original.Size, restored.Size);
        Assert.Equal(original.IsDisabled, restored.IsDisabled);
        Assert.Equal(original.PackagedPath, restored.PackagedPath);

        if (original.ProviderIdentities != null)
        {
            Assert.NotNull(restored.ProviderIdentities);
            Assert.Equal(original.ProviderIdentities.Count, restored.ProviderIdentities.Count);
        }
    }

    private static InstanceExportService CreateService() =>
        new(new AddonManifestService(), new ApplicationState(), NullLogger<InstanceExportService>.Instance);

    private static InstanceExportManifest ReadManifest(ZipArchive archive)
    {
        ZipArchiveEntry entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("manifest.json missing.");

        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<InstanceExportManifest>(
            stream,
            InstanceExportManifest.CreateJsonOptions(writeIndented: false))!;
    }

    private static InstanceMetadata ReadMetadata(ZipArchive archive)
    {
        ZipArchiveEntry entry = archive.GetEntry("pocket-mc.json")
            ?? throw new InvalidDataException("pocket-mc.json missing.");

        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<InstanceMetadata>(stream)!;
    }

    private static void AssertEntryExists(ZipArchive archive, string entryName) =>
        Assert.NotNull(archive.GetEntry(entryName));

    private static void AssertEntryMissing(ZipArchive archive, string entryName) =>
        Assert.Null(archive.GetEntry(entryName));

    private static void WriteFile(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
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
