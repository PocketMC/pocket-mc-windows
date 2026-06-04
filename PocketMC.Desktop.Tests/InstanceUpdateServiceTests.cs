using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Updates;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceUpdateServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.InstanceUpdates", Guid.NewGuid().ToString("N"));

    public InstanceUpdateServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task AddonMigrationPlan_UsesTargetMinecraftVersion_NotCurrentVersion()
    {
        string serverDir = CreateInstanceDirectory("server");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "tracked.jar"), "old");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "tracked",
            VersionId = "old-version",
            FileName = "tracked.jar"
        });

        var updateService = new RecordingAddonUpdateService();
        updateService.Results["tracked"] = UpdateResult("new-version", "tracked-new.jar");

        var planner = CreateAddonMigrationPlanner(updateService, new FakeAddonProvider("Modrinth"));
        var metadata = new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.20.1" };

        AddonMigrationPlan plan = await planner.BuildPlanAsync(
            serverDir,
            metadata,
            "1.21.1",
            new EngineCompatibility("Fabric"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons);

        Assert.Single(updateService.Calls);
        Assert.Equal("1.21.1", updateService.Calls[0].MinecraftVersion);
        Assert.Single(plan.Items);
        Assert.Equal("new-version", plan.Items[0].TargetVersionId);
    }

    [Fact]
    public async Task AddonMigrationPlan_FindsTrackedModrinthUpdate_ForTargetVersion()
    {
        string serverDir = CreateInstanceDirectory("modrinth-update");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "old.jar"), "old");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "sodium",
            VersionId = "v1",
            FileName = "old.jar"
        });

        var updateService = new RecordingAddonUpdateService();
        updateService.Results["sodium"] = UpdateResult("v2", "sodium-1.21.1.jar");

        var planner = CreateAddonMigrationPlanner(updateService, new FakeAddonProvider("Modrinth"));

        AddonMigrationPlan plan = await planner.BuildPlanAsync(
            serverDir,
            new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.20.1" },
            "1.21.1",
            new EngineCompatibility("Fabric"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons);

        AddonMigrationItem item = Assert.Single(plan.Items);
        Assert.Equal(AddonMigrationAction.Update, item.Action);
        Assert.Equal("Modrinth", item.Provider);
        Assert.Equal("sodium", item.ProjectId);
        Assert.Equal("sodium-1.21.1.jar", item.TargetFileName);
        Assert.Equal(1, plan.CompatibleUpdateCount);
    }

    [Fact]
    public async Task AddonMigrationPlan_ManualAddonBecomesWarning_NotAutoUpdate()
    {
        string serverDir = CreateInstanceDirectory("manual-addon");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "manual.jar"), "manual");

        var planner = CreateAddonMigrationPlanner(new RecordingAddonUpdateService(), new FakeAddonProvider("Modrinth"));

        AddonMigrationPlan plan = await planner.BuildPlanAsync(
            serverDir,
            new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.20.1" },
            "1.21.1",
            new EngineCompatibility("Fabric"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons);

        Assert.Empty(plan.Items);
        Assert.Equal(1, plan.ManualUntrackedAddonCount);
        AddonMigrationWarning warning = Assert.Single(plan.Warnings);
        Assert.Contains("manual.jar", warning.Message);
        Assert.Equal(AddonMigrationWarningCode.ManualAddonNotUpdated, warning.Code);
    }

    [Fact]
    public async Task FailedAddonStaging_LeavesLiveFilesUntouched()
    {
        string serverDir = CreateInstanceDirectory("failed-staging");
        string modsDir = Path.Combine(serverDir, "mods");
        Directory.CreateDirectory(modsDir);
        string livePath = Path.Combine(modsDir, "same.jar");
        File.WriteAllText(livePath, "live");

        var plan = new AddonMigrationPlan
        {
            ServerDir = serverDir,
            TargetCompatibility = new EngineCompatibility("Fabric")
        };
        plan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "same",
            CurrentFileName = "same.jar",
            TargetFileName = "same.jar",
            TargetSubDirectory = "mods",
            DownloadUrl = "https://example.invalid/same.jar",
            TargetVersionId = "v2"
        });

        var downloader = new DownloaderService(new FailingHttpClientFactory(), NullLogger<DownloaderService>.Instance);
        var stager = new AddonMigrationStager(downloader);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stager.StageAsync(plan, Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "addons")));

        Assert.Equal("live", File.ReadAllText(livePath));
    }

    [Fact]
    public async Task FailedAddonApply_RollsBackServerAndAddonFolders()
    {
        string serverDir = CreateInstanceDirectory("failed-apply");
        File.WriteAllText(Path.Combine(serverDir, ".pocket-mc.json"), "{}");
        File.WriteAllText(Path.Combine(serverDir, "server.jar"), "old-server");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "tracked.jar"), "old-addon");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "tracked",
            VersionId = "old",
            FileName = "tracked.jar"
        });

        string stagedServer = Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "server", "server.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedServer)!);
        File.WriteAllText(stagedServer, "new-server");

        InstanceUpdatePlan plan = CreateUpdatePlan(serverDir, "1.21.1");
        plan.AddonMigrationPlan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "tracked",
            CurrentFileName = "tracked.jar",
            TargetFileName = "tracked-new.jar",
            TargetSubDirectory = "mods",
            TargetVersionId = "new",
            StagedFilePath = Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "addons", "missing.jar")
        });

        var applier = CreateApplier();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            applier.ApplyAsync(plan, new InstanceUpdateStagedArtifacts { ServerArtifactPath = stagedServer }));

        Assert.Equal("old-server", File.ReadAllText(Path.Combine(serverDir, "server.jar")));
        Assert.Equal("old-addon", File.ReadAllText(Path.Combine(serverDir, "mods", "tracked.jar")));
        Assert.False(File.Exists(Path.Combine(serverDir, "mods", "tracked-new.jar")));
    }

    [Fact]
    public async Task AddonManifest_IsNotUpdatedBeforeSuccessfulApply()
    {
        string serverDir = CreateInstanceDirectory("manifest-timing");
        File.WriteAllText(Path.Combine(serverDir, ".pocket-mc.json"), "{}");
        File.WriteAllText(Path.Combine(serverDir, "server.jar"), "old-server");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "tracked.jar"), "old-addon");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "tracked",
            VersionId = "old",
            FileName = "tracked.jar"
        });

        string stagedServer = Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "server", "server.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedServer)!);
        File.WriteAllText(stagedServer, "new-server");

        InstanceUpdatePlan plan = CreateUpdatePlan(serverDir, "1.21.1");
        plan.AddonMigrationPlan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "tracked",
            CurrentFileName = "tracked.jar",
            TargetFileName = "tracked-new.jar",
            TargetSubDirectory = "mods",
            TargetVersionId = "new",
            StagedFilePath = Path.Combine(serverDir, "missing-staged-addon.jar")
        });

        var applier = CreateApplier();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            applier.ApplyAsync(plan, new InstanceUpdateStagedArtifacts { ServerArtifactPath = stagedServer }));

        AddonManifest manifest = await new AddonManifestService().LoadManifestAsync(serverDir);
        AddonManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("old", entry.VersionId);
        Assert.Equal("tracked.jar", entry.FileName);
    }

    [Fact]
    public async Task SameFilenameUpdate_DoesNotOverwriteLiveFileBeforeSnapshot()
    {
        string serverDir = CreateInstanceDirectory("same-name");
        File.WriteAllText(Path.Combine(serverDir, ".pocket-mc.json"), "{}");
        File.WriteAllText(Path.Combine(serverDir, "server.jar"), "old-server");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "same.jar"), "old-addon");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "same",
            VersionId = "old",
            FileName = "same.jar"
        });

        string stagedServer = Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "server", "server.jar");
        string stagedAddon = Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "addons", "same.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedServer)!);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedAddon)!);
        File.WriteAllText(stagedServer, "new-server");
        File.WriteAllText(stagedAddon, "new-addon");

        InstanceUpdatePlan plan = CreateUpdatePlan(serverDir, "1.21.1");
        plan.AddonMigrationPlan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "same",
            CurrentFileName = "same.jar",
            TargetFileName = "same.jar",
            TargetSubDirectory = "mods",
            TargetVersionId = "new",
            StagedFilePath = stagedAddon
        });

        InstanceUpdateApplyResult result = await CreateApplier().ApplyAsync(
            plan,
            new InstanceUpdateStagedArtifacts { ServerArtifactPath = stagedServer });

        string snapshottedAddon = Path.Combine(result.SnapshotDirectory, "mods", "same.jar");
        Assert.Equal("old-addon", File.ReadAllText(snapshottedAddon));
        Assert.Equal("new-addon", File.ReadAllText(Path.Combine(serverDir, "mods", "same.jar")));
    }

    [Fact]
    public async Task BedrockAddons_ArePreservedDuringServerApply()
    {
        string serverDir = CreateInstanceDirectory("bedrock-preserve");
        File.WriteAllText(Path.Combine(serverDir, ".pocket-mc.json"), "{}");
        Directory.CreateDirectory(Path.Combine(serverDir, "behavior_packs", "BehaviorPack"));
        Directory.CreateDirectory(Path.Combine(serverDir, "resource_packs", "ResourcePack"));
        File.WriteAllText(Path.Combine(serverDir, "behavior_packs", "BehaviorPack", "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(serverDir, "resource_packs", "ResourcePack", "manifest.json"), "{}");

        string stagedZip = Path.Combine(serverDir, ".pocketmc-updates", "staging", "op", "server", "bedrock-server.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedZip)!);
        using (ZipArchive archive = ZipFile.Open(stagedZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("bedrock_server.exe");
            await using Stream stream = entry.Open();
            await using StreamWriter writer = new(stream);
            await writer.WriteAsync("new-bedrock-server");
        }

        InstanceUpdatePlan plan = CreateUpdatePlan(serverDir, "1.21.90", "Bedrock");
        plan.ServerArtifactFileName = "bedrock-server.zip";

        await CreateApplier().ApplyAsync(plan, new InstanceUpdateStagedArtifacts { ServerArtifactPath = stagedZip });

        Assert.True(File.Exists(Path.Combine(serverDir, "behavior_packs", "BehaviorPack", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(serverDir, "resource_packs", "ResourcePack", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(serverDir, "bedrock_server.exe")));
    }

    [Fact]
    public async Task PocketMinePoggitPlugins_CanBePlanned()
    {
        string serverDir = CreateInstanceDirectory("poggit");
        Directory.CreateDirectory(Path.Combine(serverDir, "plugins"));
        File.WriteAllText(Path.Combine(serverDir, "plugins", "old.phar"), "old");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Poggit",
            ProjectId = "EssentialsPE",
            VersionId = "1.0.0",
            FileName = "old.phar"
        });

        var updateService = new RecordingAddonUpdateService();
        updateService.Results["EssentialsPE"] = UpdateResult("2.0.0", "EssentialsPE.phar");

        var planner = CreateAddonMigrationPlanner(updateService, new FakeAddonProvider("Poggit"));

        AddonMigrationPlan plan = await planner.BuildPlanAsync(
            serverDir,
            new InstanceMetadata { ServerType = "Pocketmine-MP", MinecraftVersion = "5.0.0" },
            "5.1.0",
            new EngineCompatibility("Pocketmine-MP"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons);

        AddonMigrationItem item = Assert.Single(plan.Items);
        Assert.Equal("Poggit", item.Provider);
        Assert.Equal("plugins", item.TargetSubDirectory);
        Assert.Equal("EssentialsPE.phar", item.TargetFileName);
    }

    [Fact]
    public async Task DependencyResolution_IsIncludedInMigrationPlan()
    {
        string serverDir = CreateInstanceDirectory("dependencies");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "root.jar"), "old");
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "root",
            VersionId = "old",
            FileName = "root.jar"
        });

        var updateService = new RecordingAddonUpdateService();
        updateService.Results["root"] = UpdateResult("root-new", "root-new.jar");

        var provider = new FakeAddonProvider("Modrinth");
        provider.Versions["root"] = new MarketplaceVersion
        {
            Id = "root-new",
            ProjectId = "root",
            ProjectTitle = "Root",
            FileName = "root-new.jar",
            DownloadUrl = "https://example.test/root-new.jar",
            Dependencies =
            [
                new MarketplaceDependency { ProjectId = "library", Type = DependencyType.Required }
            ]
        };
        provider.Versions["library"] = new MarketplaceVersion
        {
            Id = "library-1",
            ProjectId = "library",
            ProjectTitle = "Library",
            FileName = "library.jar",
            DownloadUrl = "https://example.test/library.jar"
        };

        AddonMigrationPlan plan = await CreateAddonMigrationPlanner(updateService, provider).BuildPlanAsync(
            serverDir,
            new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.20.1" },
            "1.21.1",
            new EngineCompatibility("Fabric"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddonsAndDependencies);

        Assert.Contains(plan.Items, item => item.Action == AddonMigrationAction.AddDependency && item.ProjectId == "library");
        Assert.Equal(1, plan.DependencyAdditionCount);
    }

    [Fact]
    public async Task IncompleteUpdateJournal_CanRollbackIdempotently()
    {
        string serverDir = CreateInstanceDirectory("journal-rollback");
        File.WriteAllText(Path.Combine(serverDir, "server.jar"), "new-server");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        File.WriteAllText(Path.Combine(serverDir, "mods", "tracked.jar"), "new-addon");

        string snapshotDir = Path.Combine(serverDir, ".pocketmc-updates", "snapshots", "operation");
        Directory.CreateDirectory(Path.Combine(snapshotDir, "mods"));
        File.WriteAllText(Path.Combine(snapshotDir, "server.jar"), "old-server");
        File.WriteAllText(Path.Combine(snapshotDir, "mods", "tracked.jar"), "old-addon");

        var journal = new InstanceUpdateJournal
        {
            OperationId = Guid.NewGuid(),
            InstanceId = Guid.NewGuid(),
            ServerDir = serverDir,
            SnapshotDirectory = snapshotDir,
            State = InstanceUpdateJournalState.ApplyingAddons
        };

        var rollback = new InstanceRollbackService(new InstanceUpdateJournalStore());

        await rollback.RollbackAsync(journal);
        await rollback.RollbackAsync(journal);

        Assert.Equal("old-server", File.ReadAllText(Path.Combine(serverDir, "server.jar")));
        Assert.Equal("old-addon", File.ReadAllText(Path.Combine(serverDir, "mods", "tracked.jar")));
    }

    [Fact]
    public async Task AddonMigrationPlan_JavaEngine_RejectsNonJarExtensions()
    {
        string serverDir = CreateInstanceDirectory("java-rejects");
        Directory.CreateDirectory(Path.Combine(serverDir, "mods"));
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Modrinth",
            ProjectId = "badaddon",
            VersionId = "old",
            FileName = "badaddon.jar"
        });

        var updateService = new RecordingAddonUpdateService();
        updateService.Results["badaddon"] = UpdateResult("new", "badaddon.mcpack"); // invalid for Java

        var planner = CreateAddonMigrationPlanner(updateService, new FakeAddonProvider("Modrinth"));

        await Assert.ThrowsAsync<NotSupportedException>(() => planner.BuildPlanAsync(
            serverDir,
            new InstanceMetadata { ServerType = "Fabric", MinecraftVersion = "1.20.1" },
            "1.21.1",
            new EngineCompatibility("Fabric"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons));
    }

    [Fact]
    public async Task AddonMigrationPlan_PocketMineEngine_RejectsNonPharExtensions()
    {
        string serverDir = CreateInstanceDirectory("pocketmine-rejects");
        Directory.CreateDirectory(Path.Combine(serverDir, "plugins"));
        await WriteManifestAsync(serverDir, new AddonManifestEntry
        {
            Provider = "Poggit",
            ProjectId = "badplugin",
            VersionId = "old",
            FileName = "badplugin.phar"
        });

        var updateService = new RecordingAddonUpdateService();
        updateService.Results["badplugin"] = UpdateResult("new", "badplugin.jar"); // invalid for PocketMine

        var planner = CreateAddonMigrationPlanner(updateService, new FakeAddonProvider("Poggit"));

        await Assert.ThrowsAsync<NotSupportedException>(() => planner.BuildPlanAsync(
            serverDir,
            new InstanceMetadata { ServerType = "Pocketmine-MP", MinecraftVersion = "5.0.0" },
            "5.1.0",
            new EngineCompatibility("Pocketmine-MP"),
            InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons));
    }

    [Fact]
    public async Task AddonMigrationStager_RejectsIncompatibleExtensions()
    {
        var plan = new AddonMigrationPlan
        {
            ServerDir = CreateInstanceDirectory("stager-rejects"),
            TargetCompatibility = new EngineCompatibility("Fabric")
        };
        plan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "bad",
            CurrentFileName = "bad.jar",
            TargetFileName = "bad.mcpack", // invalid for Fabric
            TargetSubDirectory = "mods",
            DownloadUrl = "https://example.invalid/bad.mcpack"
        });

        var downloader = new DownloaderService(new FailingHttpClientFactory(), NullLogger<DownloaderService>.Instance);
        var stager = new AddonMigrationStager(downloader);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            stager.StageAsync(plan, Path.Combine(plan.ServerDir, "staging")));
    }

    [Fact]
    public async Task SameNameMigrationFailure_PreservesExistingFile()
    {
        string serverDir = CreateInstanceDirectory("same-name-failure");
        string modsDir = Path.Combine(serverDir, "mods");
        Directory.CreateDirectory(modsDir);
        string targetPath = Path.Combine(modsDir, "same.jar");
        File.WriteAllText(targetPath, "original-content");

        var plan = new AddonMigrationPlan { ServerDir = serverDir };
        plan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "same",
            CurrentFileName = "same.jar",
            TargetFileName = "same.jar",
            TargetSubDirectory = "mods",
            TargetVersionId = "v2",
            StagedFilePath = Path.Combine(serverDir, "nonexistent-staged.jar") // will fail validation/copy
        });

        var applier = new AddonMigrationApplier(new AddonManifestService());

        await Assert.ThrowsAsync<FileNotFoundException>(() => applier.ApplyAsync(plan));
        Assert.Equal("original-content", File.ReadAllText(targetPath));
    }

    [Fact]
    public async Task RenamedMigrationFailure_DoesNotDeleteOldFile()
    {
        string serverDir = CreateInstanceDirectory("renamed-failure");
        string modsDir = Path.Combine(serverDir, "mods");
        Directory.CreateDirectory(modsDir);
        string currentPath = Path.Combine(modsDir, "old.jar");
        File.WriteAllText(currentPath, "original-content");

        var plan = new AddonMigrationPlan { ServerDir = serverDir };
        plan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "renamed",
            CurrentFileName = "old.jar",
            TargetFileName = "new.jar",
            TargetSubDirectory = "mods",
            TargetVersionId = "v2",
            StagedFilePath = Path.Combine(serverDir, "nonexistent-staged.jar") // will fail validation/copy
        });

        var applier = new AddonMigrationApplier(new AddonManifestService());

        await Assert.ThrowsAsync<FileNotFoundException>(() => applier.ApplyAsync(plan));
        Assert.True(File.Exists(currentPath));
        Assert.Equal("original-content", File.ReadAllText(currentPath));
    }

    [Fact]
    public async Task SuccessfulRenamedMigration_InstallsNewFileAndDeletesOldFile()
    {
        string serverDir = CreateInstanceDirectory("renamed-success");
        string modsDir = Path.Combine(serverDir, "mods");
        Directory.CreateDirectory(modsDir);
        string currentPath = Path.Combine(modsDir, "old.jar");
        File.WriteAllText(currentPath, "original-content");

        string stagedPath = Path.Combine(serverDir, "staged.jar");
        File.WriteAllText(stagedPath, "staged-content");

        var plan = new AddonMigrationPlan { ServerDir = serverDir };
        plan.Items.Add(new AddonMigrationItem
        {
            Action = AddonMigrationAction.Update,
            Provider = "Modrinth",
            ProjectId = "renamed",
            CurrentFileName = "old.jar",
            TargetFileName = "new.jar",
            TargetSubDirectory = "mods",
            TargetVersionId = "v2",
            StagedFilePath = stagedPath
        });

        var applier = new AddonMigrationApplier(new AddonManifestService());

        await applier.ApplyAsync(plan);

        Assert.False(File.Exists(currentPath));
        string targetPath = Path.Combine(modsDir, "new.jar");
        Assert.True(File.Exists(targetPath));
        Assert.Equal("staged-content", File.ReadAllText(targetPath));
    }

    private AddonMigrationPlanner CreateAddonMigrationPlanner(
        RecordingAddonUpdateService updateService,
        params IAddonProvider[] providers)
    {
        return new AddonMigrationPlanner(
            new AddonManifestService(),
            updateService,
            new DependencyResolverService(new AddonManifestService()),
            providers);
    }

    private InstanceUpdateApplier CreateApplier()
    {
        var journalStore = new InstanceUpdateJournalStore();
        return new InstanceUpdateApplier(
            new InstanceRollbackService(journalStore),
            new AddonMigrationApplier(new AddonManifestService()),
            journalStore,
            new FakeLifecycleService(),
            static (_, _, _) => Task.CompletedTask,
            NullLogger<InstanceUpdateApplier>.Instance);
    }

    private InstanceUpdatePlan CreateUpdatePlan(string serverDir, string targetVersion, string serverType = "Fabric")
    {
        var current = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            ServerType = serverType,
            MinecraftVersion = "1.20.1"
        };

        var target = new InstanceMetadata
        {
            Id = current.Id,
            Name = current.Name,
            ServerType = serverType,
            MinecraftVersion = targetVersion
        };

        return new InstanceUpdatePlan
        {
            OperationId = Guid.NewGuid(),
            InstanceId = current.Id,
            ServerDir = serverDir,
            CurrentMetadata = current,
            TargetMetadata = target,
            TargetMinecraftVersion = targetVersion,
            TargetCompatibility = new EngineCompatibility(serverType),
            UpdateMode = InstanceUpdateMode.ServerAndCompatibleMarketplaceAddons,
            ServerArtifactFileName = serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase)
                ? "bedrock-server.zip"
                : "server.jar",
            AddonMigrationPlan = new AddonMigrationPlan
            {
                ServerDir = serverDir,
                TargetCompatibility = new EngineCompatibility(serverType)
            }
        };
    }

    private string CreateInstanceDirectory(string name)
    {
        string path = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Task WriteManifestAsync(string serverDir, params AddonManifestEntry[] entries)
    {
        return new AddonManifestService().SaveManifestAsync(serverDir, new AddonManifest { Entries = entries.ToList() });
    }

    private static AddonUpdateCheckResult UpdateResult(string versionId, string fileName)
    {
        return new AddonUpdateCheckResult
        {
            IsUpdateAvailable = true,
            LatestVersionId = versionId,
            LatestVersionName = versionId,
            LatestFileName = fileName,
            LatestDownloadUrl = $"https://example.test/{fileName}",
            ProjectTitle = fileName
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingAddonUpdateService : AddonUpdateService
    {
        public List<(string ProjectId, string MinecraftVersion, string Loader)> Calls { get; } = new();
        public Dictionary<string, AddonUpdateCheckResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        public RecordingAddonUpdateService()
            : base(new AddonManifestService(), null!, null!, null!, new StaticHttpClientFactory(new HttpClient()))
        {
        }

        public override Task<AddonUpdateCheckResult> CheckForUpdateFromEntryAsync(
            AddonManifestEntry entry,
            string mcVersion,
            string loader,
            EngineCompatibility compat)
        {
            Calls.Add((entry.ProjectId, mcVersion, loader));
            return Task.FromResult(Results.TryGetValue(entry.ProjectId, out AddonUpdateCheckResult? result)
                ? result
                : new AddonUpdateCheckResult { Error = "No compatible version found." });
        }
    }

    private sealed class FakeAddonProvider : IAddonProvider
    {
        public FakeAddonProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public Dictionary<string, MarketplaceVersion> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader)
        {
            return Task.FromResult(Versions.GetValueOrDefault(projectId));
        }

        public Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates)
        {
            return Task.FromResult(Versions.GetValueOrDefault(projectId));
        }

        public Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId)
        {
            MarketplaceVersion? version = Versions.Values.FirstOrDefault(v => v.Id == versionId);
            return Task.FromResult(version);
        }

        public Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId)
        {
            return Task.FromResult<MarketplaceProjectInfo?>(new MarketplaceProjectInfo
            {
                Id = projectId,
                Title = projectId,
                Slug = projectId
            });
        }
    }

    private sealed class FailingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FailingHandler());
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new HttpRequestException("staging failed");
            }
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class FakeLifecycleService : IServerLifecycleService
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
        public ServerProcess? GetProcess(Guid instanceId) => null;
        public DateTime? GetSessionStartTime(Guid instanceId) => null;
        public Task ReleaseInstanceAsync(Guid instanceId) => Task.CompletedTask;
    }
}
