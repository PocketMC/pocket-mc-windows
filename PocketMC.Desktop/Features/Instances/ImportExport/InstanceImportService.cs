using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public sealed class InstanceImportService : IInstanceImportService
{
    private const int HeaderLength = 4;
    private const string ManifestEntryName = "manifest.json";
    private const string MetadataEntryName = "pocket-mc.json";
    private const string ServerRootEntryPrefix = "server/";
    private const string ServerPropertiesEntryName = "server/server.properties";
    private const string StagingDirectoryName = ".staging";
    private const string DownloadsDirectoryName = ".downloads";
    private const int MaxParallelAddonDownloads = 3;

    private static readonly byte[] LocalFileHeader = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] EmptyArchiveHeader = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] SpannedArchiveHeader = [0x50, 0x4B, 0x07, 0x08];

    private readonly InstancePathService _pathService;
    private readonly InstanceRegistry _registry;
    private readonly IReadOnlyList<IServerSoftwareProvider> _softwareProviders;
    private readonly IReadOnlyDictionary<string, IAddonProvider> _addonProviders;
    private readonly DownloaderService _downloader;
    private readonly MarketplaceFileInstaller _marketplaceFileInstaller;
    private readonly AddonManifestService _addonManifestService;
    private readonly BedrockAddonInstaller _bedrockAddonInstaller;
    private readonly ApplicationState _applicationState;
    private readonly ILogger<InstanceImportService> _logger;
    private readonly SemaphoreSlim _addonManifestUpdateLock = new(1, 1);
    private CancellationTokenSource? _activeCts;

    public bool IsActive => _activeCts != null;
    public void Cancel() => _activeCts?.Cancel();

    public InstanceImportService(
        InstancePathService pathService,
        InstanceRegistry registry,
        IEnumerable<IServerSoftwareProvider> softwareProviders,
        IEnumerable<IAddonProvider> addonProviders,
        DownloaderService downloader,
        MarketplaceFileInstaller marketplaceFileInstaller,
        AddonManifestService addonManifestService,
        BedrockAddonInstaller bedrockAddonInstaller,
        ApplicationState applicationState,
        ILogger<InstanceImportService> logger)
    {
        _pathService = pathService;
        _registry = registry;
        _softwareProviders = softwareProviders.ToArray();
        _addonProviders = addonProviders.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
        _downloader = downloader;
        _marketplaceFileInstaller = marketplaceFileInstaller;
        _addonManifestService = addonManifestService;
        _bedrockAddonInstaller = bedrockAddonInstaller;
        _applicationState = applicationState;
        _logger = logger;
    }

    public async Task<InstanceExportManifest> ReadManifestAsync(
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        string fullZipPath = ValidateZipPath(zipPath);
        await ValidateZipHeaderAsync(fullZipPath, cancellationToken).ConfigureAwait(false);

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(fullZipPath);
            ValidateArchiveEntries(archive);

            ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException("Import ZIP is missing manifest.json.");

            await using Stream stream = manifestEntry.Open();
            InstanceExportManifest manifest = await JsonSerializer.DeserializeAsync<InstanceExportManifest>(
                    stream,
                    InstanceExportManifest.CreateJsonOptions(),
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("Import manifest is empty.");

            ValidateManifest(manifest);
            return manifest;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Import manifest is not valid Pocket MC JSON.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException("Import ZIP could not be read.", ex);
        }
    }

    public async Task<InstanceImportStagingResult> StageImportAsync(
        InstanceImportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string fullZipPath = ValidateZipPath(request.ZipPath);
        Report(progress, "Validating import package...", 0);
        InstanceExportManifest manifest = await ReadManifestAsync(fullZipPath, cancellationToken).ConfigureAwait(false);

        Guid operationId = Guid.NewGuid();
        string stagingRoot = Path.Combine(_pathService.GetServersRoot(), StagingDirectoryName);
        string stagingDirectory = Path.Combine(stagingRoot, operationId.ToString("N"));

        try
        {
            _pathService.EnsureServersRootExists();
            Directory.CreateDirectory(stagingRoot);
            SetHidden(stagingRoot);

            Directory.CreateDirectory(stagingDirectory);
            SetHidden(stagingDirectory);

            Report(progress, "Extracting import package...", 10);
            await SafeZipExtractor.ExtractAsync(
                    fullZipPath,
                    stagingDirectory,
                    (completed, total) =>
                    {
                        double extractionProgress = total <= 0
                            ? 90
                            : 10 + Math.Clamp(completed * 80d / total, 0, 80);

                        Report(progress, "Extracting import package...", extractionProgress);
                    })
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, "Scrubbing import-only backup state...", 92);
            await ScrubStagedBackupStateAsync(stagingDirectory, cancellationToken).ConfigureAwait(false);

            Report(progress, "Verifying staged import...", 95);
            ValidateStagedImport(stagingDirectory, manifest);

            Report(progress, "Import package staged.", 100);
            return new InstanceImportStagingResult
            {
                OperationId = operationId,
                StagingDirectory = stagingDirectory,
                ServerDirectory = Path.Combine(stagingDirectory, "server"),
                MetadataPath = Path.Combine(stagingDirectory, MetadataEntryName),
                Manifest = manifest
            };
        }
        catch
        {
            await CleanupStagingDirectoryAsync(stagingDirectory).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ReconstructStagedImportAsync(
        InstanceImportStagingResult stagingResult,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagingResult);
        ValidateStagedImport(stagingResult.StagingDirectory, stagingResult.Manifest);

        Directory.CreateDirectory(stagingResult.ServerDirectory);
        Directory.CreateDirectory(GetDownloadsDirectory(stagingResult));

        Report(progress, "Downloading server software...", 0);
        await DownloadServerSoftwareAsync(stagingResult, progress, cancellationToken).ConfigureAwait(false);

        Report(progress, "Downloading add-ons...", 40);
        await DownloadAddonsAsync(stagingResult, progress, cancellationToken).ConfigureAwait(false);

        Report(progress, "Reconstruction complete.", 100);
    }

    public async Task<InstanceImportResult> ImportAsync(
        InstanceImportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        InstanceImportStagingResult? stagingResult = null;
        bool promoted = false;

        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken linkedToken = _activeCts.Token;

        try
        {
            stagingResult = await StageImportAsync(
                    request,
                    CreateMappedProgress(progress, 0, 25),
                    linkedToken)
                .ConfigureAwait(false);

            await ReconstructStagedImportAsync(
                    stagingResult,
                    CreateMappedProgress(progress, 25, 90),
                    linkedToken)
                .ConfigureAwait(false);

            Report(progress, "Preparing imported instance...", 90);
            
            InstanceMetadata metadata = await ReadImportedMetadataAsync(stagingResult.MetadataPath, linkedToken)
                .ConfigureAwait(false);
            
            string finalPath = ResolveUniqueInstancePath(FirstNonEmpty(request.RequestedName, stagingResult.Manifest.ServerMeta.Name, metadata.Name, "Imported Server"));
            
            await PrepareStagedServerForPromotionAsync(metadata, stagingResult, request, finalPath, linkedToken)
                .ConfigureAwait(false);

            Report(progress, "Promoting imported instance...", 95);
            Directory.Move(stagingResult.ServerDirectory, finalPath);
            promoted = true;

            _registry.Register(metadata, finalPath);
            await CleanupStagingDirectoryAsync(stagingResult.StagingDirectory).ConfigureAwait(false);
            Report(progress, "Import complete.", 100);

            return new InstanceImportResult
            {
                InstanceId = metadata.Id,
                InstancePath = finalPath,
                Metadata = metadata,
                Manifest = stagingResult.Manifest,
                Report = stagingResult.Report
            };
        }
        catch
        {
            if (stagingResult != null && !promoted)
            {
                await CleanupStagingDirectoryAsync(stagingResult.StagingDirectory).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    private async Task PrepareStagedServerForPromotionAsync(
        InstanceMetadata metadata,
        InstanceImportStagingResult stagingResult,
        InstanceImportRequest request,
        string finalPath,
        CancellationToken cancellationToken)
    {
        InstanceExportManifest manifest = stagingResult.Manifest;

        metadata.Id = Guid.NewGuid();
        metadata.Name = FirstNonEmpty(request.RequestedName, manifest.ServerMeta.Name, metadata.Name, "Imported Server");
        metadata.Description = FirstNonEmpty(manifest.ServerMeta.Description, metadata.Description);
        metadata.ServerType = ToPocketMcServerType(manifest.Software);
        metadata.MinecraftVersion = manifest.Software.MinecraftVersion;
        metadata.LoaderVersion = manifest.Software.LoaderVersion ?? string.Empty;
        metadata.CreatedAt = DateTime.UtcNow;
        metadata.LastPlayedAt = null;
        metadata.LastBackupTime = null;

        // Tunnel state must be cleared because it's machine specific.
        metadata.SimpleVoiceChatTunnelId = null;
        metadata.SimpleVoiceChatTunnelAddress = null;
        metadata.SimpleVoiceChatNumericTunnelAddress = null;
        metadata.SimpleVoiceChatStatus = null;
        metadata.SimpleVoiceChatLastWarning = null;
        metadata.SimpleVoiceChatDetected = false;
        metadata.SimpleVoiceChatConfigPath = null;

        string appRoot = _applicationState.IsConfigured ? _applicationState.GetRequiredAppRootPath() : string.Empty;
        metadata.CustomJavaPath = RestorePortablePath(metadata.CustomJavaPath, appRoot, isFile: true);
        metadata.CustomBackupDirectory = null;

        await WriteMetadataAsync(Path.Combine(stagingResult.ServerDirectory, InstancePathService.MetadataFileName), metadata, cancellationToken)
            .ConfigureAwait(false);
        await CopyIconIntoStagedServerAsync(stagingResult, cancellationToken).ConfigureAwait(false);
    }

    private static string? RestorePortablePath(string? relativePath, string appRoot, bool isFile)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(relativePath))
            {
                if (isFile && File.Exists(relativePath)) return relativePath;
                if (!isFile && Directory.Exists(relativePath)) return relativePath;
                return null;
            }

            if (string.IsNullOrWhiteSpace(appRoot))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(Path.Combine(appRoot, relativePath));

            if (isFile && File.Exists(fullPath)) return fullPath;
            if (!isFile && Directory.Exists(fullPath)) return fullPath;
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    private static async Task ScrubStagedBackupStateAsync(
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        string? serverDirectory = PathSafety.ValidateContainedPath(stagingDirectory, "server");
        if (serverDirectory == null || !Directory.Exists(serverDirectory))
        {
            return;
        }

        string? backupsDirectory = PathSafety.ValidateContainedPath(serverDirectory, "backups");
        if (backupsDirectory != null && Directory.Exists(backupsDirectory))
        {
            await FileUtils.CleanDirectoryAsync(backupsDirectory, cancellationToken).ConfigureAwait(false);
        }

        string? legacyManifestPath = PathSafety.ValidateContainedPath(serverDirectory, "backup-manifest.json");
        if (legacyManifestPath != null)
        {
            TryDeleteFile(legacyManifestPath);
        }
    }

    private static async Task<InstanceMetadata> ReadImportedMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return new InstanceMetadata();
        }

        try
        {
            await using var stream = new FileStream(metadataPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

            return await JsonSerializer.DeserializeAsync<InstanceMetadata>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                ?? new InstanceMetadata();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("Imported pocket-mc.json could not be read as instance metadata.", ex);
        }
    }

    private static async Task WriteMetadataAsync(
        string metadataPath,
        InstanceMetadata metadata,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await FileUtils.AtomicWriteAllTextAsync(metadataPath, json, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CopyIconIntoStagedServerAsync(
        InstanceImportStagingResult stagingResult,
        CancellationToken cancellationToken)
    {
        string? iconPath = stagingResult.Manifest.ServerMeta.Icon;
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        string? sourceIcon = PathSafety.ValidateContainedPath(stagingResult.StagingDirectory, iconPath);
        if (sourceIcon == null || !File.Exists(sourceIcon))
        {
            return;
        }

        string destinationIcon = Path.Combine(stagingResult.ServerDirectory, "server-icon.png");
        await using var sourceStream = new FileStream(sourceIcon, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = 81920,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using var destinationStream = new FileStream(destinationIcon, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 81920,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveUniqueInstancePath(string instanceName)
    {
        string baseSlug = SlugHelper.GenerateSlug(instanceName);
        string slug = baseSlug;
        int counter = 2;

        while (Directory.Exists(_pathService.GetInstancePath(slug)))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return _pathService.GetInstancePath(slug);
    }

    private static string ToPocketMcServerType(ServerSoftwareManifest software)
    {
        if (software.Platform == InstanceServerPlatform.Bedrock)
        {
            return software.Type.Equals("BDS", StringComparison.OrdinalIgnoreCase)
                ? "Bedrock"
                : software.Type;
        }

        return string.IsNullOrWhiteSpace(software.Type) ? "Vanilla" : software.Type;
    }

    private async Task DownloadServerSoftwareAsync(
        InstanceImportStagingResult stagingResult,
        IProgress<InstanceTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ServerSoftwareManifest software = stagingResult.Manifest.Software;

        switch (software.Platform)
        {
            case InstanceServerPlatform.Java:
                await DownloadJavaServerSoftwareAsync(stagingResult.ServerDirectory, software, progress, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case InstanceServerPlatform.Bedrock:
                await DownloadBedrockServerSoftwareAsync(stagingResult, software, progress, cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                throw new InvalidDataException($"Unsupported server platform '{software.Platform}'.");
        }
    }

    private async Task DownloadJavaServerSoftwareAsync(
        string serverDirectory,
        ServerSoftwareManifest software,
        IProgress<InstanceTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        string serverType = software.Type;
        string minecraftVersion = software.MinecraftVersion;
        string loaderVersion = software.LoaderVersion ?? string.Empty;
        string artifactFileName = IsInstallerBasedJavaServer(serverType) ? "installer.jar" : "server.jar";
        string artifactPath = Path.Combine(serverDirectory, artifactFileName);
        IServerSoftwareProvider provider = ResolveSoftwareProvider(serverType);

        var downloadProgress = CreateDownloadProgress(progress, "Downloading server software...", 0, 40, artifactFileName);

        if (provider is FabricProvider fabricProvider &&
            serverType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(loaderVersion))
        {
            await fabricProvider.DownloadFabricJarAsync(minecraftVersion, loaderVersion, artifactPath, downloadProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (provider is ForgeProvider forgeProvider &&
                 serverType.Equals("Forge", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(loaderVersion))
        {
            await forgeProvider.DownloadForgeJarAsync(minecraftVersion, loaderVersion, artifactPath, downloadProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (provider is NeoForgeProvider neoForgeProvider &&
                 serverType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(loaderVersion))
        {
            await neoForgeProvider.DownloadNeoForgeJarAsync(minecraftVersion, loaderVersion, artifactPath, downloadProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await provider.DownloadSoftwareAsync(minecraftVersion, artifactPath, downloadProgress, cancellationToken)
                .ConfigureAwait(false);
        }

        ValidateNonEmptyFile(artifactPath, "server software");
    }

    private async Task DownloadBedrockServerSoftwareAsync(
        InstanceImportStagingResult stagingResult,
        ServerSoftwareManifest software,
        IProgress<InstanceTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (software.Type.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
        {
            await DownloadPocketmineSoftwareAsync(stagingResult.ServerDirectory, software, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        IServerSoftwareProvider provider = ResolveSoftwareProvider(software.Type);
        string downloadsDirectory = GetDownloadsDirectory(stagingResult);
        string bdsZipPath = Path.Combine(downloadsDirectory, $"bds-{Guid.NewGuid():N}.zip");
        string bdsExtractDirectory = Path.Combine(downloadsDirectory, $"bds-extract-{Guid.NewGuid():N}");

        try
        {
            var downloadProgress = CreateDownloadProgress(progress, "Downloading Bedrock server...", 0, 25, "BDS");
            await provider.DownloadSoftwareAsync(software.MinecraftVersion, bdsZipPath, downloadProgress, cancellationToken)
                .ConfigureAwait(false);
            ValidateNonEmptyFile(bdsZipPath, "Bedrock server archive");

            Report(progress, "Extracting Bedrock server...", 25, "BDS");
            await _downloader.ExtractZipAsync(
                    bdsZipPath,
                    bdsExtractDirectory,
                    CreateDownloadProgress(progress, "Extracting Bedrock server...", 25, 35, "BDS"))
                .ConfigureAwait(false);

            await CopyDirectoryPreservingExistingAsync(bdsExtractDirectory, stagingResult.ServerDirectory, cancellationToken)
                .ConfigureAwait(false);

            string executablePath = Path.Combine(stagingResult.ServerDirectory, "bedrock_server.exe");
            ValidateNonEmptyFile(executablePath, "Bedrock server executable");
        }
        finally
        {
            TryDeleteFile(bdsZipPath);
            TryDeleteFile(bdsZipPath + ".partial");
            await TryDeleteDirectoryAsync(bdsExtractDirectory).ConfigureAwait(false);
        }
    }

    private async Task DownloadPocketmineSoftwareAsync(
        string serverDirectory,
        ServerSoftwareManifest software,
        IProgress<InstanceTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        IServerSoftwareProvider provider = ResolveSoftwareProvider("Pocketmine");
        string artifactPath = Path.Combine(serverDirectory, "PocketMine-MP.phar");
        await provider.DownloadSoftwareAsync(
                software.MinecraftVersion,
                artifactPath,
                CreateDownloadProgress(progress, "Downloading PocketMine-MP...", 0, 40, "PocketMine-MP.phar"),
                cancellationToken)
            .ConfigureAwait(false);

        ValidateNonEmptyFile(artifactPath, "PocketMine-MP runtime");
    }

    private async Task DownloadAddonsAsync(
        InstanceImportStagingResult stagingResult,
        IProgress<InstanceTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InstanceAddonManifest> allAddons = stagingResult.Manifest.Addons;

        var report = stagingResult.Report;
        report.InstanceName = stagingResult.Manifest.ServerMeta.Name;
        report.TotalAddons = allAddons.Count;

        if (allAddons.Count == 0)
        {
            Report(progress, "No add-ons to restore.", 100);
            return;
        }

        var addonProgress = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        foreach (var addon in allAddons)
        {
            addonProgress[addon.Name] = 0;
        }

        object progressLock = new object();
        double lastReportedOverall = 40;

        void ReportAddonProgress(string addonName, double localPercentage)
        {
            addonProgress[addonName] = localPercentage;
            lock (progressLock)
            {
                double sum = 0;
                foreach (var name in addonProgress.Keys)
                {
                    sum += addonProgress[name];
                }
                double avg = sum / allAddons.Count;
                double overall = 40 + (avg * 0.6); // Map 0-100% of addons to 40-100% of reconstruction
                if (overall > lastReportedOverall)
                {
                    lastReportedOverall = overall;
                    Report(progress, "Restoring add-ons...", overall, addonName);
                }
            }
        }

        using var semaphore = new SemaphoreSlim(MaxParallelAddonDownloads);
        var reportEntries = new System.Collections.Concurrent.ConcurrentBag<AddonImportReportEntry>();

        var tasks = allAddons.Select(async addon =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            string? resolutionSource = null;
            string status = "failed";
            string? reason = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportAddonProgress(addon.Name, 0);

                // Step 1: Try to restore from the packaged file in the ZIP
                bool restoredFromPackage = false;
                if (addon is JavaAddonManifest javaAddon)
                {
                    restoredFromPackage = await TryRestorePackagedJavaAddonAsync(
                            stagingResult, javaAddon, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (restoredFromPackage)
                {
                    resolutionSource = "Packaged File";
                    status = "restoredFromPackage";
                    reason = "Restored from packaged file in export ZIP.";
                    ReportAddonProgress(addon.Name, 100);
                    reportEntries.Add(new AddonImportReportEntry
                    {
                        Name = addon.Name,
                        Provider = addon.Provider,
                        FileName = addon.FileName ?? "",
                        Success = true,
                        ResolutionSource = resolutionSource,
                        Status = status,
                        Reason = reason
                    });
                    return;
                }

                // Step 2: For Local-only addons with no download capability, skip
                if (addon.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase) &&
                    addon is JavaAddonManifest localJava &&
                    string.IsNullOrWhiteSpace(localJava.DownloadUrl) &&
                    string.IsNullOrWhiteSpace(localJava.ProjectId) &&
                    (addon.ProviderIdentities == null || addon.ProviderIdentities.Count == 0))
                {
                    status = "skipped";
                    reason = "Local-only addon with no download source and no packaged file.";
                    ReportAddonProgress(addon.Name, 100);
                    reportEntries.Add(new AddonImportReportEntry
                    {
                        Name = addon.Name,
                        Provider = addon.Provider,
                        FileName = addon.FileName ?? "",
                        Success = false,
                        ResolutionSource = "None",
                        Status = status,
                        Reason = reason
                    });
                    return;
                }

                // Step 3: Fall back to provider download
                switch (stagingResult.Manifest.Software.Platform)
                {
                    case InstanceServerPlatform.Java:
                        resolutionSource = await DownloadJavaAddonAsync(stagingResult, (JavaAddonManifest)addon, ReportAddonProgress, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case InstanceServerPlatform.Bedrock:
                        resolutionSource = await DownloadBedrockAddonAsync(stagingResult, (BedrockAddonManifest)addon, ReportAddonProgress, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                }

                status = "downloadedFromProvider";
                reason = $"Downloaded from {resolutionSource ?? addon.Provider}.";
                ReportAddonProgress(addon.Name, 100);
                reportEntries.Add(new AddonImportReportEntry
                {
                    Name = addon.Name,
                    Provider = addon.Provider,
                    FileName = addon.FileName ?? "",
                    Success = true,
                    ResolutionSource = resolutionSource ?? "Unknown",
                    Status = status,
                    Reason = reason
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore add-on {AddonName}.", addon.Name);
                ReportAddonProgress(addon.Name, 100); // Mark as done for progress calculation
                reportEntries.Add(new AddonImportReportEntry
                {
                    Name = addon.Name,
                    Provider = addon.Provider,
                    FileName = addon.FileName ?? "",
                    Success = false,
                    ResolutionSource = resolutionSource ?? "None",
                    ErrorMessage = ex.Message,
                    Status = "failed",
                    Reason = ex.Message
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Populate report results
        report.Addons.AddRange(reportEntries);
        report.SuccessfulAddons = report.Addons.Count(a => a.Success);
        report.FailedAddons = report.Addons.Count(a => !a.Success);
        report.RestoredFromPackage = report.Addons.Count(a => a.Status == "restoredFromPackage");
        report.DownloadedFromProvider = report.Addons.Count(a => a.Status == "downloadedFromProvider");
        report.Skipped = report.Addons.Count(a => a.Status == "skipped");
        report.Failed = report.Addons.Count(a => a.Status == "failed");

        // Save detailed import report
        try
        {
            string reportPath = Path.Combine(stagingResult.ServerDirectory, "import_report.json");
            string reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await FileUtils.AtomicWriteAllTextAsync(reportPath, reportJson, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write import_report.json to server directory.");
        }
    }

    /// <summary>
    /// Attempts to restore a Java addon from its packaged file in the extracted ZIP.
    /// Returns true if the file was found and restored, false if it needs to be downloaded.
    /// </summary>
    private async Task<bool> TryRestorePackagedJavaAddonAsync(
        InstanceImportStagingResult stagingResult,
        JavaAddonManifest addon,
        CancellationToken cancellationToken)
    {
        // Determine possible source paths in the staging directory
        string? sourceFilePath = null;

        // Try PackagedPath first (relative to staging root, e.g. "server/mods/MyMod.jar")
        if (!string.IsNullOrWhiteSpace(addon.PackagedPath))
        {
            string candidate = Path.Combine(stagingResult.StagingDirectory, addon.PackagedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                sourceFilePath = candidate;
            }
        }

        // Try RelativePath (relative to server directory, e.g. "mods/MyMod.jar")
        if (sourceFilePath == null && !string.IsNullOrWhiteSpace(addon.RelativePath))
        {
            string candidate = Path.Combine(stagingResult.ServerDirectory, addon.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                sourceFilePath = candidate;
            }
        }

        // Try FileName in the addon type directory
        if (sourceFilePath == null && !string.IsNullOrWhiteSpace(addon.FileName))
        {
            string addonDirectory = ResolveJavaAddonDirectory(stagingResult.ServerDirectory, addon.Type);
            string candidate = Path.Combine(addonDirectory, addon.FileName);
            if (File.Exists(candidate))
            {
                sourceFilePath = candidate;
            }

            // Also check for disabled variant
            if (sourceFilePath == null && AddonFileNamePolicy.IsEnabledJarFileName(addon.FileName))
            {
                string disabledCandidate = Path.Combine(addonDirectory, AddonFileNamePolicy.GetDisabledFileName(addon.FileName));
                if (File.Exists(disabledCandidate))
                {
                    sourceFilePath = disabledCandidate;
                }
            }
        }

        if (sourceFilePath == null || !File.Exists(sourceFilePath))
        {
            return false;
        }

        // Verify file is not empty
        var fileInfo = new FileInfo(sourceFilePath);
        if (fileInfo.Length <= 0)
        {
            return false;
        }

        // Verify size if metadata is available
        if (addon.Size.HasValue && addon.Size.Value > 0 && fileInfo.Length != addon.Size.Value)
        {
            _logger.LogWarning(
                "Packaged file size mismatch for {AddonName}: expected {Expected} bytes, got {Actual} bytes. Falling back to download.",
                addon.Name, addon.Size.Value, fileInfo.Length);
            return false;
        }

        // Determine the correct destination based on disabled state
        string destinationDirectory = ResolveJavaAddonDirectory(stagingResult.ServerDirectory, addon.Type);
        string enabledFileName = addon.FileName ?? Path.GetFileName(sourceFilePath);

        // Normalize the enabled filename (strip disabled suffix if present)
        if (AddonFileNamePolicy.IsDisabledJarFileName(enabledFileName))
        {
            enabledFileName = AddonFileNamePolicy.GetOriginalFileNameFromDisabled(enabledFileName);
        }

        string finalFileName;
        if (addon.IsDisabled && AddonFileNamePolicy.IsEnabledJarFileName(enabledFileName))
        {
            finalFileName = AddonFileNamePolicy.GetDisabledFileName(enabledFileName);
        }
        else
        {
            finalFileName = enabledFileName;
        }

        string destinationPath = Path.Combine(destinationDirectory, finalFileName);

        // Move/copy the file to the correct location if needed
        if (!sourceFilePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(destinationDirectory);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourceFilePath, destinationPath);
        }

        // Register addon install in the local manifest using provider identities
        await RegisterRestoredAddonAsync(stagingResult, addon, enabledFileName, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Registers a restored-from-package addon in the local addon_manifest.json,
    /// using the primary provider identity if available.
    /// </summary>
    private async Task RegisterRestoredAddonAsync(
        InstanceImportStagingResult stagingResult,
        JavaAddonManifest addon,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Determine the best provider identity
        string provider = addon.Provider;
        string projectId = addon.ProjectId ?? string.Empty;
        string versionId = addon.VersionId ?? string.Empty;

        if (addon.ProviderIdentities is { Count: > 0 })
        {
            ProviderIdentity primary = addon.ProviderIdentities[0];
            if (!string.IsNullOrWhiteSpace(primary.Provider))
            {
                provider = primary.Provider;
                projectId = primary.ProjectId;
                versionId = primary.VersionId;
            }
        }

        // Only register if this is actually a tracked addon (not purely local)
        if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        await _addonManifestUpdateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? hash = null;
            string? hashType = null;

            if (!string.IsNullOrWhiteSpace(addon.Sha512))
            {
                hash = addon.Sha512;
                hashType = "sha512";
            }
            else if (!string.IsNullOrWhiteSpace(addon.Hash))
            {
                int separator = addon.Hash.IndexOf('-', StringComparison.Ordinal);
                if (separator > 0 && separator < addon.Hash.Length - 1)
                {
                    hashType = addon.Hash[..separator];
                    hash = addon.Hash[(separator + 1)..];
                }
                else
                {
                    hash = addon.Hash;
                }
            }

            await _addonManifestService.RegisterInstallAsync(
                    stagingResult.ServerDirectory,
                    provider,
                    projectId,
                    versionId,
                    fileName,
                    addon.Name,
                    null,
                    addon.Name,
                    null,
                    null,
                    hash,
                    hashType,
                    stagingResult.Manifest.Software.MinecraftVersion,
                    addon.Loader,
                    addon is JavaAddonManifest java ? java.DownloadUrl : null)
                .ConfigureAwait(false);

            // Register additional provider identities
            if (addon.ProviderIdentities is { Count: > 1 })
            {
                for (int i = 1; i < addon.ProviderIdentities.Count; i++)
                {
                    ProviderIdentity identity = addon.ProviderIdentities[i];
                    if (!string.IsNullOrWhiteSpace(identity.Provider) &&
                        !identity.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
                    {
                        await _addonManifestService.RegisterInstallAsync(
                                stagingResult.ServerDirectory,
                                identity.Provider,
                                identity.ProjectId,
                                identity.VersionId,
                                fileName,
                                addon.Name,
                                null,
                                addon.Name,
                                null,
                                null,
                                hash,
                                hashType,
                                stagingResult.Manifest.Software.MinecraftVersion,
                                addon.Loader)
                            .ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            _addonManifestUpdateLock.Release();
        }
    }

    private async Task<string> DownloadJavaAddonAsync(
        InstanceImportStagingResult stagingResult,
        JavaAddonManifest addon,
        Action<string, double> reportProgressAction,
        CancellationToken cancellationToken)
    {
        string destinationDirectory = ResolveJavaAddonDirectory(stagingResult.ServerDirectory, addon.Type);
        string? destinationPath = null;

        // Try direct download first if DownloadUrl is available
        if (!string.IsNullOrWhiteSpace(addon.DownloadUrl))
        {
            try
            {
                string fileName = RequireSafeAddonFileName(
                    FirstNonEmpty(addon.FileName, $"{addon.Name}.jar"),
                    ".jar");
                destinationPath = Path.Combine(destinationDirectory, fileName);
                HashSpec hash = ResolveHash(null, null, addon.Hash);

                var directProgress = new Progress<DownloadProgress>(download =>
                {
                    double localPercentage = download.TotalBytes > 0 ? download.Percentage : 0;
                    reportProgressAction(addon.Name, localPercentage);
                });

                await _marketplaceFileInstaller.InstallAsync(
                        addon.DownloadUrl,
                        destinationPath,
                        hash.Hash,
                        hash.HashType,
                        directProgress,
                        cancellationToken)
                    .ConfigureAwait(false);

                await RegisterJavaAddonInstallDirectAsync(
                        stagingResult,
                        addon,
                        fileName,
                        hash,
                        cancellationToken)
                    .ConfigureAwait(false);

                return "Direct Download URL";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Direct download failed for Java add-on {AddonName} from {Url}. Falling back to Modrinth resolution.", addon.Name, addon.DownloadUrl);
                if (!string.IsNullOrWhiteSpace(destinationPath))
                {
                    TryDeleteFile(destinationPath);
                    TryDeleteFile(destinationPath + ".partial");
                }
            }
        }

        IAddonProvider provider = ResolveAddonProvider(addon.Provider, addon);
        IReadOnlyList<JavaAddonDownloadCandidate> candidates = await ResolveJavaAddonDownloadCandidatesAsync(
                provider,
                addon,
                stagingResult.Manifest,
                cancellationToken)
            .ConfigureAwait(false);

        var failures = new List<string>();
        Exception? lastException = null;

        foreach (JavaAddonDownloadCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarketplaceVersion version = candidate.Version;
            try
            {
                string fileName = RequireSafeAddonFileName(
                    FirstNonEmpty(version.FileName, addon.FileName, $"{addon.Name}.jar"),
                    ".jar");
                destinationPath = Path.Combine(destinationDirectory, fileName);
                HashSpec hash = ResolveJavaCandidateHash(candidate, addon);

                var candidateProgress = new Progress<DownloadProgress>(download =>
                {
                    double localPercentage = download.TotalBytes > 0 ? download.Percentage : 0;
                    reportProgressAction(addon.Name, localPercentage);
                });

                await _marketplaceFileInstaller.InstallAsync(
                        version.DownloadUrl,
                        destinationPath,
                        hash.Hash,
                        hash.HashType,
                        candidateProgress,
                        cancellationToken)
                    .ConfigureAwait(false);

                await RegisterJavaAddonInstallAsync(
                        stagingResult,
                        addon,
                        provider,
                        version,
                        fileName,
                        hash,
                        cancellationToken)
                    .ConfigureAwait(false);

                return candidate.Source;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not AddonUnavailableException)
            {
                lastException = ex;
                failures.Add($"{candidate.Source}: {ex.Message}");
                _logger.LogWarning(
                    ex,
                    "Failed to restore Java add-on {AddonName} from {Provider} using {ResolutionSource}. Trying another candidate when available.",
                    addon.Name,
                    provider.Name,
                    candidate.Source);

                if (!string.IsNullOrWhiteSpace(destinationPath))
                {
                    TryDeleteFile(destinationPath);
                    TryDeleteFile(destinationPath + ".partial");
                }
            }
        }

        throw new AddonUnavailableException(
            addon.Name,
            provider.Name,
            addon.RelativePath,
            BuildJavaAddonUnavailableMessage(addon, provider.Name, failures),
            lastException);
    }

    private async Task RegisterJavaAddonInstallAsync(
        InstanceImportStagingResult stagingResult,
        JavaAddonManifest addon,
        IAddonProvider provider,
        MarketplaceVersion version,
        string fileName,
        HashSpec hash,
        CancellationToken cancellationToken)
    {
        await _addonManifestUpdateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _addonManifestService.RegisterInstallAsync(
                    stagingResult.ServerDirectory,
                    provider.Name,
                    FirstNonEmpty(version.ProjectId, addon.ProjectId),
                    FirstNonEmpty(version.Id, addon.VersionId),
                    fileName,
                    version.ProjectTitle,
                    version.IconUrl,
                    FirstNonEmpty(version.ProjectTitle, addon.Name),
                    version.ClientSide,
                    version.ServerSide,
                    hash.Hash,
                    hash.HashType,
                    stagingResult.Manifest.Software.MinecraftVersion,
                    FirstNonEmpty(version.SelectedLoader, addon.Loader, stagingResult.Manifest.Software.Type))
                .ConfigureAwait(false);
        }
        finally
        {
            _addonManifestUpdateLock.Release();
        }
    }

    private async Task RegisterJavaAddonInstallDirectAsync(
        InstanceImportStagingResult stagingResult,
        JavaAddonManifest addon,
        string fileName,
        HashSpec hash,
        CancellationToken cancellationToken)
    {
        await _addonManifestUpdateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _addonManifestService.RegisterInstallAsync(
                    stagingResult.ServerDirectory,
                    addon.Provider,
                    addon.ProjectId ?? string.Empty,
                    addon.VersionId ?? string.Empty,
                    fileName,
                    addon.Name,
                    null,
                    addon.Name,
                    null,
                    null,
                    hash.Hash,
                    hash.HashType,
                    stagingResult.Manifest.Software.MinecraftVersion,
                    addon.Loader,
                    addon.DownloadUrl)
                .ConfigureAwait(false);
        }
        finally
        {
            _addonManifestUpdateLock.Release();
        }
    }

    private async Task<IReadOnlyList<JavaAddonDownloadCandidate>> ResolveJavaAddonDownloadCandidatesAsync(
        IAddonProvider provider,
        JavaAddonManifest addon,
        InstanceExportManifest manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<JavaAddonDownloadCandidate>();
        IReadOnlyList<string> loaderCandidates = BuildJavaLoaderCandidates(addon, manifest.Software);
        HashSpec manifestHash = ResolveHash(null, null, addon.Hash);

        if (provider is ModrinthService modrinthService &&
            !string.IsNullOrWhiteSpace(manifestHash.Hash))
        {
            MarketplaceVersion? hashVersion = await modrinthService
                .GetVersionByHashAsync(manifestHash.Hash, manifestHash.HashType, loaderCandidates)
                .ConfigureAwait(false);
            AddJavaAddonCandidate(candidates, hashVersion, "manifest file hash", preferManifestHash: true);
        }

        if (!string.IsNullOrWhiteSpace(addon.VersionId))
        {
            string providerVersionId = BuildProviderVersionId(provider.Name, addon);
            MarketplaceVersion? exactVersion = await provider.GetVersionByIdAsync(providerVersionId)
                .ConfigureAwait(false);
            AddJavaAddonCandidate(candidates, exactVersion, "manifest version id", preferManifestHash: true);
        }

        if (!string.IsNullOrWhiteSpace(addon.ProjectId))
        {
            MarketplaceVersion? latestVersion = await provider
                .GetLatestVersionAsync(addon.ProjectId, manifest.Software.MinecraftVersion, loaderCandidates)
                .ConfigureAwait(false);
            AddJavaAddonCandidate(candidates, latestVersion, "manifest project id");
        }

        if (provider is ModrinthService searchableModrinth)
        {
            foreach (string query in BuildJavaAddonSearchQueries(addon))
            {
                cancellationToken.ThrowIfCancellationRequested();
                MarketplaceVersion? searchVersion = await searchableModrinth
                    .FindVersionBySearchAsync(query, addon.Type, manifest.Software.MinecraftVersion, loaderCandidates)
                    .ConfigureAwait(false);
                AddJavaAddonCandidate(candidates, searchVersion, $"Modrinth search '{query}'");
            }
        }

        if (candidates.Count == 0)
        {
            throw new AddonUnavailableException(
                addon.Name,
                provider.Name,
                addon.RelativePath,
                BuildJavaAddonUnavailableMessage(addon, provider.Name, Array.Empty<string>()));
        }

        return candidates;
    }

    private async Task<string> DownloadBedrockAddonAsync(
        InstanceImportStagingResult stagingResult,
        BedrockAddonManifest addon,
        Action<string, double> reportProgressAction,
        CancellationToken cancellationToken)
    {
        string downloadsDirectory = GetDownloadsDirectory(stagingResult);
        Directory.CreateDirectory(downloadsDirectory);

        string downloadUrl = addon.DownloadUrl ?? string.Empty;
        string fileName = RequireSafeAddonFileName(FirstNonEmpty(addon.FileName, $"{addon.Name}.mcpack"), ".mcpack", ".mcaddon", ".zip");
        HashSpec hash = ResolveHash(null, null, addon.Hash);
        string source = "Direct Download URL";

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            IAddonProvider provider = ResolveAddonProvider(addon.Provider, addon);
            MarketplaceVersion version = await ResolveMarketplaceVersionAsync(provider, addon, stagingResult.Manifest, cancellationToken)
                .ConfigureAwait(false);

            downloadUrl = version.DownloadUrl;
            fileName = RequireSafeAddonFileName(FirstNonEmpty(version.FileName, addon.FileName, $"{addon.Name}.mcpack"), ".mcpack", ".mcaddon", ".zip");
            hash = ResolveHash(version.Hash, version.HashType, addon.Hash);
            source = provider.Name;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new AddonUnavailableException(addon.Name, addon.Provider, addon.RelativePath, "No download URL was available for this Bedrock add-on.");
        }

        string archivePath = Path.Combine(downloadsDirectory, fileName);
        try
        {
            var bedrockProgress = new Progress<DownloadProgress>(download =>
            {
                double localPercentage = download.TotalBytes > 0 ? download.Percentage : 0;
                reportProgressAction(addon.Name, localPercentage);
            });

            await _marketplaceFileInstaller.InstallAsync(
                    downloadUrl,
                    archivePath,
                    hash.Hash,
                    hash.HashType,
                    bedrockProgress,
                    cancellationToken)
                .ConfigureAwait(false);

            await _bedrockAddonInstaller.InstallAsync(archivePath, stagingResult.ServerDirectory, cancellationToken)
                .ConfigureAwait(false);

            return source;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AddonUnavailableException)
        {
            throw new AddonUnavailableException(addon.Name, addon.Provider, addon.RelativePath, "The Bedrock add-on could not be downloaded or installed.", ex);
        }
    }

    private async Task<MarketplaceVersion> ResolveMarketplaceVersionAsync(
        IAddonProvider provider,
        JavaAddonManifest addon,
        InstanceExportManifest manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MarketplaceVersion? version = null;
        if (!string.IsNullOrWhiteSpace(addon.VersionId))
        {
            string versionId = provider.Name.Equals("CurseForge", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrWhiteSpace(addon.ProjectId)
                ? $"{addon.ProjectId}:{addon.VersionId}"
                : addon.VersionId;

            version = await provider.GetVersionByIdAsync(versionId).ConfigureAwait(false);
        }

        if (version == null && !string.IsNullOrWhiteSpace(addon.ProjectId))
        {
            string loader = FirstNonEmpty(addon.Loader, manifest.Software.Type);
            version = await provider.GetLatestVersionAsync(addon.ProjectId, manifest.Software.MinecraftVersion, loader)
                .ConfigureAwait(false);
        }

        ValidateMarketplaceVersion(version, addon);
        return version!;
    }

    private async Task<MarketplaceVersion> ResolveMarketplaceVersionAsync(
        IAddonProvider provider,
        BedrockAddonManifest addon,
        InstanceExportManifest manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MarketplaceVersion? version = null;
        string? versionId = addon.Version;
        string? projectId = addon.Uuid;

        if (!string.IsNullOrWhiteSpace(versionId))
        {
            string providerVersionId = provider.Name.Equals("CurseForge", StringComparison.OrdinalIgnoreCase) &&
                                       !string.IsNullOrWhiteSpace(projectId)
                ? $"{projectId}:{versionId}"
                : versionId;

            version = await provider.GetVersionByIdAsync(providerVersionId).ConfigureAwait(false);
        }

        if (version == null && !string.IsNullOrWhiteSpace(projectId))
        {
            version = await provider.GetLatestVersionAsync(projectId, manifest.Software.MinecraftVersion, "bedrock")
                .ConfigureAwait(false);
        }

        ValidateMarketplaceVersion(version, addon);
        return version!;
    }

    private static void ValidateMarketplaceVersion(MarketplaceVersion? version, InstanceAddonManifest addon)
    {
        if (version == null)
        {
            throw new AddonUnavailableException(addon.Name, addon.Provider, addon.RelativePath, "The provider did not return a matching version.");
        }

        if (string.IsNullOrWhiteSpace(version.DownloadUrl))
        {
            throw new AddonUnavailableException(addon.Name, addon.Provider, addon.RelativePath, "The provider returned a version without a download URL.");
        }
    }

    private static IReadOnlyList<string> BuildJavaLoaderCandidates(
        JavaAddonManifest addon,
        ServerSoftwareManifest software)
    {
        var loaders = new List<string>();
        AddLoaderCandidate(loaders, addon.Loader);
        AddLoaderCandidate(loaders, software.Type);

        string primaryLoader = loaders.FirstOrDefault() ?? NormalizeLoaderName(software.Type);
        switch (primaryLoader)
        {
            case "paper":
                AddLoaderCandidate(loaders, "spigot");
                AddLoaderCandidate(loaders, "bukkit");
                break;
            case "purpur":
                AddLoaderCandidate(loaders, "paper");
                AddLoaderCandidate(loaders, "spigot");
                AddLoaderCandidate(loaders, "bukkit");
                break;
            case "spigot":
                AddLoaderCandidate(loaders, "bukkit");
                break;
            case "bukkit":
                AddLoaderCandidate(loaders, "spigot");
                AddLoaderCandidate(loaders, "paper");
                break;
            case "quilt":
                AddLoaderCandidate(loaders, "fabric");
                break;
        }

        if (addon.Type.Equals(InstanceAddonTypes.Plugin, StringComparison.OrdinalIgnoreCase) &&
            !loaders.Any(IsKnownPluginLoader))
        {
            AddLoaderCandidate(loaders, "paper");
            AddLoaderCandidate(loaders, "spigot");
            AddLoaderCandidate(loaders, "bukkit");
        }

        if (loaders.Count == 0)
        {
            loaders.Add(string.Empty);
        }

        return loaders;
    }

    private static void AddLoaderCandidate(List<string> loaders, string? loader)
    {
        string normalized = NormalizeLoaderName(loader);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Equals("vanilla", StringComparison.OrdinalIgnoreCase) ||
            loaders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        loaders.Add(normalized);
    }

    private static string NormalizeLoaderName(string? loader)
    {
        if (string.IsNullOrWhiteSpace(loader))
        {
            return string.Empty;
        }

        string normalized = loader.Trim().ToLowerInvariant();
        return normalized switch
        {
            _ when normalized.StartsWith("paper", StringComparison.OrdinalIgnoreCase) => "paper",
            _ when normalized.StartsWith("purpur", StringComparison.OrdinalIgnoreCase) => "purpur",
            _ when normalized.StartsWith("spigot", StringComparison.OrdinalIgnoreCase) => "spigot",
            _ when normalized.StartsWith("bukkit", StringComparison.OrdinalIgnoreCase) => "bukkit",
            _ when normalized.StartsWith("fabric", StringComparison.OrdinalIgnoreCase) => "fabric",
            _ when normalized.StartsWith("forge", StringComparison.OrdinalIgnoreCase) => "forge",
            _ when normalized.StartsWith("neoforge", StringComparison.OrdinalIgnoreCase) => "neoforge",
            _ when normalized.StartsWith("quilt", StringComparison.OrdinalIgnoreCase) => "quilt",
            _ => normalized
        };
    }

    private static bool IsKnownPluginLoader(string loader) =>
        loader.Equals("paper", StringComparison.OrdinalIgnoreCase) ||
        loader.Equals("spigot", StringComparison.OrdinalIgnoreCase) ||
        loader.Equals("bukkit", StringComparison.OrdinalIgnoreCase) ||
        loader.Equals("purpur", StringComparison.OrdinalIgnoreCase);

    private static string BuildProviderVersionId(string providerName, JavaAddonManifest addon)
    {
        if (providerName.Equals("CurseForge", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(addon.ProjectId) &&
            !string.IsNullOrWhiteSpace(addon.VersionId) &&
            !addon.VersionId.Contains(':', StringComparison.Ordinal))
        {
            return $"{addon.ProjectId}:{addon.VersionId}";
        }

        return addon.VersionId ?? string.Empty;
    }

    private static IEnumerable<string> BuildJavaAddonSearchQueries(JavaAddonManifest addon)
    {
        var queries = new List<string>();
        AddSearchQuery(queries, addon.Name);

        string? fileStem = string.IsNullOrWhiteSpace(addon.FileName)
            ? null
            : Path.GetFileNameWithoutExtension(addon.FileName);
        AddSearchQuery(queries, fileStem);

        string stem = FirstNonEmpty(fileStem, addon.Name);
        foreach (string marker in new[] { "-fabric", "_fabric", "+fabric", "-forge", "_forge", "+forge", "-neoforge", "_neoforge", "+neoforge", "-quilt", "_quilt", "+quilt" })
        {
            int markerIndex = stem.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                AddSearchQuery(queries, stem[..markerIndex]);
            }
        }

        return queries;
    }

    private static void AddSearchQuery(List<string> queries, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            queries.Contains(query, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        queries.Add(query);
    }

    private static void AddJavaAddonCandidate(
        List<JavaAddonDownloadCandidate> candidates,
        MarketplaceVersion? version,
        string source,
        bool preferManifestHash = false)
    {
        if (version == null || string.IsNullOrWhiteSpace(version.DownloadUrl))
        {
            return;
        }

        if (candidates.Any(candidate => IsSameJavaAddonCandidate(candidate.Version, version)))
        {
            return;
        }

        candidates.Add(new JavaAddonDownloadCandidate(version, source, preferManifestHash));
    }

    private static HashSpec ResolveJavaCandidateHash(
        JavaAddonDownloadCandidate candidate,
        JavaAddonManifest addon)
    {
        if (candidate.PreferManifestHash)
        {
            HashSpec manifestHash = ResolveHash(null, null, addon.Hash);
            if (!string.IsNullOrWhiteSpace(manifestHash.Hash))
            {
                return manifestHash;
            }
        }

        return ResolveHash(candidate.Version.Hash, candidate.Version.HashType, addon.Hash);
    }

    private static bool IsSameJavaAddonCandidate(MarketplaceVersion left, MarketplaceVersion right)
    {
        if (!string.IsNullOrWhiteSpace(left.DownloadUrl) &&
            left.DownloadUrl.Equals(right.DownloadUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.Id) &&
               !string.IsNullOrWhiteSpace(right.Id) &&
               left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase) &&
               left.FileName.Equals(right.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildJavaAddonUnavailableMessage(
        JavaAddonManifest addon,
        string provider,
        IReadOnlyList<string> failures)
    {
        string baseMessage =
            $"PocketMC could not restore add-on '{addon.Name}' from {provider}. It tried the manifest hash, exact version id, project id lookup, provider search, and alternate download URLs before rolling back.";

        if (failures.Count == 0)
        {
            return baseMessage + " The provider did not return any compatible downloadable file.";
        }

        return baseMessage + Environment.NewLine + "Last attempts:" + Environment.NewLine + string.Join(Environment.NewLine, failures.TakeLast(4));
    }

    private IServerSoftwareProvider ResolveSoftwareProvider(string serverType)
    {
        string normalized = serverType ?? string.Empty;
        string providerPrefix =
            normalized.StartsWith("Paper", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Spigot", StringComparison.OrdinalIgnoreCase)
                ? "Paper"
                : normalized.StartsWith("Fabric", StringComparison.OrdinalIgnoreCase)
                    ? "Fabric"
                    : normalized.StartsWith("Forge", StringComparison.OrdinalIgnoreCase)
                        ? "Forge"
                        : normalized.StartsWith("NeoForge", StringComparison.OrdinalIgnoreCase)
                            ? "NeoForge"
                            : normalized.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase)
                                ? "Pocketmine"
                                : normalized.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) ||
                                  normalized.Equals("BDS", StringComparison.OrdinalIgnoreCase)
                                    ? "Bedrock"
                                    : "Vanilla";

        IServerSoftwareProvider? provider = _softwareProviders.FirstOrDefault(candidate =>
            candidate.DisplayName.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase));

        return provider ?? throw new InvalidOperationException($"No {providerPrefix} server software provider is registered.");
    }

    private IAddonProvider ResolveAddonProvider(string providerName, InstanceAddonManifest addon)
    {
        if (_addonProviders.TryGetValue(providerName, out IAddonProvider? provider))
        {
            return provider;
        }

        throw new AddonUnavailableException(addon.Name, providerName, addon.RelativePath, $"The add-on provider '{providerName}' is not registered.");
    }

    private static string ResolveJavaAddonDirectory(string serverDirectory, string addonType)
    {
        string directoryName = addonType switch
        {
            InstanceAddonTypes.Plugin => "plugins",
            InstanceAddonTypes.Mod => "mods",
            InstanceAddonTypes.Datapack => Path.Combine("world", "datapacks"),
            _ => "mods"
        };

        string directory = Path.Combine(serverDirectory, directoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string RequireSafeAddonFileName(string fileName, params string[] allowedExtensions)
    {
        string safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !safeName.Equals(fileName, StringComparison.Ordinal) ||
            safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            safeName.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Add-on file name '{fileName}' is unsafe.");
        }

        string extension = Path.GetExtension(safeName);
        if (!allowedExtensions.Any(allowed => extension.Equals(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Add-on file '{fileName}' has an unsupported extension.");
        }

        return safeName;
    }

    private static bool IsInstallerBasedJavaServer(string serverType) =>
        serverType.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
        serverType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase);

    private static string GetDownloadsDirectory(InstanceImportStagingResult stagingResult) =>
        Path.Combine(stagingResult.StagingDirectory, DownloadsDirectoryName);

    private static IProgress<DownloadProgress> CreateDownloadProgress(
        IProgress<InstanceTransferProgress>? progress,
        string step,
        double start,
        double end,
        string? currentItem)
    {
        return new Progress<DownloadProgress>(download =>
        {
            double localPercentage = download.TotalBytes > 0 ? download.Percentage : 0;
            double overall = start + ((end - start) * Math.Clamp(localPercentage, 0, 100) / 100d);
            Report(progress, step, overall, currentItem, localPercentage);
        });
    }

    private static IProgress<InstanceTransferProgress>? CreateMappedProgress(
        IProgress<InstanceTransferProgress>? progress,
        double start,
        double end)
    {
        if (progress == null)
        {
            return null;
        }

        return new Progress<InstanceTransferProgress>(inner =>
        {
            double localPercentage = Math.Clamp(inner.OverallProgress, 0, 100);
            double overall = start + ((end - start) * localPercentage / 100d);
            progress.Report(new InstanceTransferProgress
            {
                CurrentStep = inner.CurrentStep,
                OverallProgress = Math.Clamp(overall, 0, 100),
                DownloadProgress = inner.DownloadProgress,
                CurrentItem = inner.CurrentItem
            });
        });
    }

    private static double CalculateAddonOverallProgress(int completed, int total)
    {
        if (total <= 0)
        {
            return 100;
        }

        return 40 + Math.Clamp(completed * 60d / total, 0, 60);
    }

    private static HashSpec ResolveHash(string? versionHash, string? versionHashType, string? manifestHash)
    {
        if (!string.IsNullOrWhiteSpace(versionHash))
        {
            return new HashSpec(versionHash, versionHashType);
        }

        if (string.IsNullOrWhiteSpace(manifestHash))
        {
            return new HashSpec(null, null);
        }

        int separatorIndex = manifestHash.IndexOf('-', StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < manifestHash.Length - 1)
        {
            return new HashSpec(manifestHash[(separatorIndex + 1)..], manifestHash[..separatorIndex]);
        }

        return new HashSpec(manifestHash, null);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static void ValidateNonEmptyFile(string filePath, string description)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length <= 0)
        {
            throw new IOException($"Downloaded {description} is missing or empty at '{filePath}'.");
        }
    }

    private static async Task CopyDirectoryPreservingExistingAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                string destinationPath = Path.Combine(destinationDirectory, relativePath);

                if (File.Exists(destinationPath))
                {
                    continue;
                }

                string? destinationParent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationParent))
                {
                    Directory.CreateDirectory(destinationParent);
                }

                File.Copy(sourceFile, destinationPath, overwrite: false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryDeleteDirectoryAsync(string directory)
    {
        try
        {
            await FileUtils.CleanDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string ValidateZipPath(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new ArgumentException("Import ZIP path is required.", nameof(zipPath));
        }

        string fullPath = Path.GetFullPath(zipPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Import ZIP '{fullPath}' does not exist.", fullPath);
        }

        if (!fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Import package must use a .zip file extension.");
        }

        return fullPath;
    }

    private static async Task ValidateZipHeaderAsync(string zipPath, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderLength];

        await using var stream = new FileStream(zipPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = HeaderLength,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        int read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (read < HeaderLength || !HasSupportedZipHeader(header))
        {
            throw new InvalidDataException("Import package is not a valid ZIP archive.");
        }
    }

    private static bool HasSupportedZipHeader(byte[] header) =>
        header.AsSpan().SequenceEqual(LocalFileHeader) ||
        header.AsSpan().SequenceEqual(EmptyArchiveHeader) ||
        header.AsSpan().SequenceEqual(SpannedArchiveHeader);

    private static void ValidateArchiveEntries(ZipArchive archive)
    {
        if (archive.GetEntry(ManifestEntryName) == null)
        {
            throw new InvalidDataException("Import ZIP is missing manifest.json.");
        }

        if (archive.GetEntry(MetadataEntryName) == null)
        {
            throw new InvalidDataException("Import ZIP is missing pocket-mc.json.");
        }

        if (archive.GetEntry(ServerPropertiesEntryName) == null)
        {
            throw new InvalidDataException("Import ZIP is missing server/server.properties.");
        }

        bool hasServerContent = false;
        string validationRoot = Path.Combine(Path.GetTempPath(), "PocketMCImportValidation");

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                throw new InvalidDataException("Import ZIP contains an unnamed entry.");
            }

            if (PathSafety.ValidateContainedPath(validationRoot, entry.FullName) == null)
            {
                throw new InvalidDataException($"ZIP entry '{entry.FullName}' would extract outside the staging directory.");
            }

            string normalized = NormalizeZipEntryName(entry.FullName);
            if (normalized.StartsWith(ServerRootEntryPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(entry.Name))
            {
                hasServerContent = true;
            }
        }

        if (!hasServerContent)
        {
            throw new InvalidDataException("Import ZIP does not contain server content.");
        }
    }

    private static void ValidateManifest(InstanceExportManifest manifest)
    {
        if (!manifest.ExportVersion.Equals(InstanceExportManifest.CurrentExportVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported import manifest version '{manifest.ExportVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ServerMeta.Name))
        {
            throw new InvalidDataException("Import manifest is missing serverMeta.name.");
        }

        if (manifest.ServerMeta.Icon != null)
        {
            ValidateManifestRelativePath(manifest.ServerMeta.Icon, "serverMeta.icon");
        }

        if (string.IsNullOrWhiteSpace(manifest.Software.Type))
        {
            throw new InvalidDataException("Import manifest is missing software.type.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Software.MinecraftVersion))
        {
            throw new InvalidDataException("Import manifest is missing software.minecraftVersion.");
        }

        if (manifest.Software.Platform == InstanceServerPlatform.Java &&
            manifest.Runtime.Type != InstanceRuntimeType.Java)
        {
            throw new InvalidDataException("Java imports must declare a Java runtime.");
        }

        if (manifest.Software.Platform == InstanceServerPlatform.Bedrock &&
            manifest.Runtime.Type != InstanceRuntimeType.Native)
        {
            throw new InvalidDataException("Bedrock imports must declare a Native runtime.");
        }

        foreach (InstanceAddonManifest addon in manifest.Addons)
        {
            ValidateAddonManifest(addon, manifest.Software.Platform);
        }
    }

    private static void ValidateAddonManifest(InstanceAddonManifest addon, InstanceServerPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(addon.Name))
        {
            throw new InvalidDataException("Import manifest contains an add-on without a name.");
        }

        if (string.IsNullOrWhiteSpace(addon.Type))
        {
            throw new InvalidDataException($"Import manifest add-on '{addon.Name}' is missing a type.");
        }

        if (string.IsNullOrWhiteSpace(addon.Provider))
        {
            throw new InvalidDataException($"Import manifest add-on '{addon.Name}' is missing a provider.");
        }

        if (addon.FileName != null)
        {
            ValidateManifestFileName(addon.FileName, $"add-ons[{addon.Name}].fileName");
        }

        if (addon.RelativePath != null)
        {
            ValidateManifestRelativePath(addon.RelativePath, $"add-ons[{addon.Name}].relativePath");
        }

        switch (addon)
        {
            case JavaAddonManifest java:
                if (platform != InstanceServerPlatform.Java)
                {
                    throw new InvalidDataException($"Java add-on '{addon.Name}' cannot be imported into a Bedrock server.");
                }

                if (!addon.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(java.ProjectId) &&
                    string.IsNullOrWhiteSpace(java.VersionId) &&
                    string.IsNullOrWhiteSpace(java.Hash) &&
                    string.IsNullOrWhiteSpace(addon.PackagedPath) &&
                    string.IsNullOrWhiteSpace(java.DownloadUrl) &&
                    (addon.ProviderIdentities == null || addon.ProviderIdentities.Count == 0))
                {
                    throw new InvalidDataException($"Remote Java add-on '{addon.Name}' is missing projectId, versionId, hash, packagedPath, or providerIdentities.");
                }

                break;

            case BedrockAddonManifest:
                if (platform != InstanceServerPlatform.Bedrock)
                {
                    throw new InvalidDataException($"Bedrock add-on '{addon.Name}' cannot be imported into a Java server.");
                }

                break;
        }
    }

    private static void ValidateManifestRelativePath(string relativePath, string propertyName)
    {
        if (PathSafety.ContainsTraversal(relativePath))
        {
            throw new InvalidDataException($"Import manifest path '{propertyName}' is unsafe.");
        }
    }

    private static void ValidateManifestFileName(string fileName, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.GetFileName(fileName) != fileName ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Import manifest file name '{propertyName}' is unsafe.");
        }
    }

    private static void ValidateStagedImport(string stagingDirectory, InstanceExportManifest manifest)
    {
        ValidateContainedStagedPath(stagingDirectory, ManifestEntryName, requiredFile: true);
        ValidateContainedStagedPath(stagingDirectory, MetadataEntryName, requiredFile: true);
        ValidateContainedStagedPath(stagingDirectory, ServerPropertiesEntryName, requiredFile: true);

        string serverDirectory = Path.Combine(stagingDirectory, "server");
        if (!Directory.Exists(serverDirectory))
        {
            throw new InvalidDataException("Staged import is missing the server directory.");
        }

        if (manifest.ServerMeta.Icon != null)
        {
            ValidateContainedStagedPath(stagingDirectory, manifest.ServerMeta.Icon, requiredFile: false);
        }
    }

    private static void ValidateContainedStagedPath(string stagingDirectory, string relativePath, bool requiredFile)
    {
        string? path = PathSafety.ValidateContainedPath(stagingDirectory, relativePath);
        if (path == null)
        {
            throw new InvalidDataException($"Staged import path '{relativePath}' escapes the staging directory.");
        }

        if (requiredFile && !File.Exists(path))
        {
            throw new InvalidDataException($"Staged import is missing '{relativePath}'.");
        }
    }

    private async Task CleanupStagingDirectoryAsync(string stagingDirectory)
    {
        try
        {
            await FileUtils.CleanDirectoryAsync(stagingDirectory, CancellationToken.None).ConfigureAwait(false);
            TryDeleteEmptyDirectory(Path.GetDirectoryName(stagingDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to clean up import staging directory {StagingDirectory}.", stagingDirectory);
        }
    }

    private static void TryDeleteEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string NormalizeZipEntryName(string entryName) =>
        entryName.Replace('\\', '/').TrimStart('/');

    private static void SetHidden(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch
        {
            // Hidden is a UX hint only; staging safety does not depend on it.
        }
    }

    private static void Report(
        IProgress<InstanceTransferProgress>? progress,
        string step,
        double overallProgress,
        string? currentItem = null,
        double downloadProgress = 0)
    {
        progress?.Report(new InstanceTransferProgress
        {
            CurrentStep = step,
            OverallProgress = Math.Clamp(overallProgress, 0, 100),
            DownloadProgress = Math.Clamp(downloadProgress, 0, 100),
            CurrentItem = currentItem
        });
    }

    private sealed record JavaAddonDownloadCandidate(
        MarketplaceVersion Version,
        string Source,
        bool PreferManifestHash);

    private sealed record HashSpec(string? Hash, string? HashType);
}
