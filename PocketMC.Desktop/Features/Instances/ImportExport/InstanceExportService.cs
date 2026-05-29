using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public sealed class InstanceExportService : IInstanceExportService
{
    private const int StreamBufferSize = 1024 * 128;
    private const string ExportedMetadataFileName = "pocket-mc.json";
    private const string SourceMetadataFileName = ".pocket-mc.json";
    private const string ServerIconFileName = "server-icon.png";

    private static readonly string[] JavaRootFiles =
    [
        "server.properties",
        "whitelist.json",
        "ops.json",
        "banned-players.json",
        "banned-ips.json",
        "bukkit.yml",
        "spigot.yml",
        "paper.yml",
        "paper-global.yml",
        "paper-world-defaults.yml",
        "commands.yml",
        "permissions.yml"
    ];

    private static readonly string[] BedrockRootFiles =
    [
        "server.properties",
        "allowlist.json",
        "permissions.json",
        "valid_known_packs.json"
    ];

    private static readonly string[] JavaDirectories =
    [
        "config",
        "mods",
        "plugins",
        "world",
        "world_nether",
        "world_the_end"
    ];

    private static readonly string[] BedrockDirectories =
    [
        "behavior_packs",
        "resource_packs",
        "worlds"
    ];

    private static readonly HashSet<string> SkippedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.lock"
    };

    private static readonly HashSet<string> BedrockBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".dll",
        ".pdb"
    };

    private readonly AddonManifestService _addonManifestService;
    private readonly ILogger<InstanceExportService> _logger;

    public InstanceExportService(AddonManifestService addonManifestService, ILogger<InstanceExportService> logger)
    {
        _addonManifestService = addonManifestService;
        _logger = logger;
    }

    public async Task<InstanceExportResult> ExportAsync(
        InstanceExportRequest request,
        IProgress<InstanceTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string instanceRoot = ValidateInstanceRoot(request.InstancePath);
        string destinationZipPath = ValidateDestinationPath(request.DestinationZipPath, instanceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationZipPath)!);

        InstanceMetadata metadata = request.Metadata ?? throw new ArgumentException("Export metadata is required.", nameof(request));
        bool isJava = metadata.Compatibility.IsJavaEngine;
        string tempZipPath = Path.Combine(
            Path.GetDirectoryName(destinationZipPath)!,
            $".{Path.GetFileName(destinationZipPath)}.{Guid.NewGuid():N}.tmp");
        var skippedFiles = new List<string>();

        try
        {
            Report(progress, "Preparing export manifest...", 0);
            InstanceExportManifest manifest = await BuildManifestAsync(metadata, instanceRoot, isJava, cancellationToken)
                .ConfigureAwait(false);
            long totalBytes = EstimateExportBytes(instanceRoot, isJava, request.IncludeWorlds, skippedFiles, cancellationToken);
            long copiedBytes = 0;

            await using (var zipStream = new FileStream(tempZipPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = StreamBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            }))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await AddJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken).ConfigureAwait(false);
                await AddMetadataEntryAsync(archive, metadata, instanceRoot, cancellationToken).ConfigureAwait(false);
                await AddIconEntryAsync(archive, instanceRoot, cancellationToken).ConfigureAwait(false);

                await foreach (ExportFile file in EnumerateExportFilesAsync(instanceRoot, isJava, request.IncludeWorlds, skippedFiles, cancellationToken)
                    .ConfigureAwait(false))
                {
                    string entryName = ToZipEntryName(Path.Combine("server", file.RelativePath));
                    Report(progress, $"Exporting {file.RelativePath}...", CalculateProgress(copiedBytes, totalBytes), file.RelativePath);

                    await AddFileEntryAsync(archive, file.FullPath, entryName, cancellationToken).ConfigureAwait(false);
                    copiedBytes += file.SizeBytes;
                }
            }

            ReplaceDestination(tempZipPath, destinationZipPath);
            var info = new FileInfo(destinationZipPath);
            Report(progress, "Export complete.", 100);

            return new InstanceExportResult
            {
                ZipPath = destinationZipPath,
                SizeBytes = info.Exists ? info.Length : 0,
                Manifest = manifest,
                SkippedFiles = skippedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
        catch
        {
            TryDeleteFile(tempZipPath);
            throw;
        }
    }

    private async Task<InstanceExportManifest> BuildManifestAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        bool isJava,
        CancellationToken cancellationToken)
    {
        string iconPath = Path.Combine(instanceRoot, ServerIconFileName);
        var manifest = new InstanceExportManifest
        {
            Origin = new InstanceExportOrigin
            {
                PocketMcVersion = GetPocketMcVersion(),
                Timestamp = DateTimeOffset.UtcNow
            },
            ServerMeta = new InstanceExportServerMeta
            {
                Name = metadata.Name,
                Description = metadata.Description,
                Icon = File.Exists(iconPath) ? "meta/icon.png" : null
            },
            Software = BuildSoftwareManifest(metadata, isJava),
            Runtime = BuildRuntimeManifest(metadata, isJava)
        };

        manifest.Addons.AddRange(await BuildAddonManifestAsync(metadata, instanceRoot, isJava, cancellationToken)
            .ConfigureAwait(false));
        return manifest;
    }

    private static ServerSoftwareManifest BuildSoftwareManifest(InstanceMetadata metadata, bool isJava)
    {
        ServerSoftwareManifest software = isJava
            ? new JavaServerSoftwareManifest()
            : new BedrockServerSoftwareManifest();

        software.Type = NormalizeServerType(metadata.ServerType, isJava);
        software.MinecraftVersion = metadata.MinecraftVersion;
        software.LoaderVersion = string.IsNullOrWhiteSpace(metadata.LoaderVersion) ? null : metadata.LoaderVersion;
        return software;
    }

    private static InstanceRuntimeManifest BuildRuntimeManifest(InstanceMetadata metadata, bool isJava)
    {
        if (!isJava)
        {
            return new InstanceRuntimeManifest { Type = InstanceRuntimeType.Native };
        }

        int javaVersion = JavaRuntimeResolver.GetRequiredJavaVersion(metadata.MinecraftVersion);
        return new InstanceRuntimeManifest
        {
            Type = InstanceRuntimeType.Java,
            TargetVersion = javaVersion.ToString()
        };
    }

    private async Task<IReadOnlyList<InstanceAddonManifest>> BuildAddonManifestAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        bool isJava,
        CancellationToken cancellationToken)
    {
        var addons = new List<InstanceAddonManifest>();
        AddonManifest providerManifest = await _addonManifestService.LoadManifestAsync(instanceRoot).ConfigureAwait(false);

        if (isJava)
        {
            AddJavaProviderAddons(addons, metadata, instanceRoot, providerManifest);
            AddLocalJavaAddons(addons, metadata, instanceRoot, providerManifest, cancellationToken);
        }
        else
        {
            AddBedrockProviderAddons(addons, instanceRoot, providerManifest);
            await AddLocalBedrockPackAddonsAsync(addons, instanceRoot, cancellationToken).ConfigureAwait(false);
        }

        return addons
            .DistinctBy(addon => $"{addon.Provider}|{addon.Type}|{GetAddonIdentity(addon)}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(addon => addon.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddJavaProviderAddons(
        List<InstanceAddonManifest> addons,
        InstanceMetadata metadata,
        string instanceRoot,
        AddonManifest providerManifest)
    {
        foreach (AddonManifestEntry entry in providerManifest.Entries)
        {
            if (!LooksLikeJavaAddon(entry.FileName))
            {
                continue;
            }

            string addonType = ResolveJavaAddonType(metadata, instanceRoot, entry.FileName);
            addons.Add(new JavaAddonManifest
            {
                Name = FirstNonEmpty(entry.DisplayName, entry.ProjectTitle, Path.GetFileNameWithoutExtension(entry.FileName)),
                Type = addonType,
                Provider = FirstNonEmpty(entry.Provider, "Local"),
                ProjectId = EmptyToNull(entry.ProjectId),
                VersionId = EmptyToNull(entry.VersionId),
                Hash = FormatHash(entry.FileHashType, entry.FileHash),
                Loader = EmptyToNull(entry.Loader ?? metadata.Compatibility.LoaderName),
                FileName = EmptyToNull(entry.FileName),
                RelativePath = ResolveAddonRelativePath(instanceRoot, addonType, entry.FileName)
            });
        }
    }

    private static void AddLocalJavaAddons(
        List<InstanceAddonManifest> addons,
        InstanceMetadata metadata,
        string instanceRoot,
        AddonManifest providerManifest,
        CancellationToken cancellationToken)
    {
        foreach ((string directoryName, string addonType) in new[]
        {
            ("plugins", InstanceAddonTypes.Plugin),
            ("mods", InstanceAddonTypes.Mod)
        })
        {
            string directory = Path.Combine(instanceRoot, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(file);

                if (providerManifest.Entries.Any(entry => entry.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)) ||
                    addons.Any(addon => addon.FileName?.Equals(fileName, StringComparison.OrdinalIgnoreCase) == true))
                {
                    continue;
                }

                addons.Add(new JavaAddonManifest
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    Type = addonType,
                    Provider = "Local",
                    FileName = fileName,
                    RelativePath = ToPortableRelativePath(instanceRoot, file),
                    Loader = metadata.Compatibility.LoaderName
                });
            }
        }
    }

    private static void AddBedrockProviderAddons(
        List<InstanceAddonManifest> addons,
        string instanceRoot,
        AddonManifest providerManifest)
    {
        foreach (AddonManifestEntry entry in providerManifest.Entries)
        {
            if (!LooksLikeBedrockAddon(entry.FileName))
            {
                continue;
            }

            string addonType = ResolveBedrockAddonType(instanceRoot, entry.FileName);
            addons.Add(new BedrockAddonManifest
            {
                Name = FirstNonEmpty(entry.DisplayName, entry.ProjectTitle, Path.GetFileNameWithoutExtension(entry.FileName)),
                Type = addonType,
                Provider = FirstNonEmpty(entry.Provider, "Local"),
                FileName = EmptyToNull(entry.FileName),
                RelativePath = ResolveAddonRelativePath(instanceRoot, addonType, entry.FileName),
                Hash = FormatHash(entry.FileHashType, entry.FileHash)
            });
        }
    }

    private static async Task AddLocalBedrockPackAddonsAsync(
        List<InstanceAddonManifest> addons,
        string instanceRoot,
        CancellationToken cancellationToken)
    {
        await AddLocalBedrockPackDirectoryAsync(
            addons,
            Path.Combine(instanceRoot, "behavior_packs"),
            instanceRoot,
            InstanceAddonTypes.BehaviorPack,
            cancellationToken).ConfigureAwait(false);

        await AddLocalBedrockPackDirectoryAsync(
            addons,
            Path.Combine(instanceRoot, "resource_packs"),
            instanceRoot,
            InstanceAddonTypes.ResourcePack,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddLocalBedrockPackDirectoryAsync(
        List<InstanceAddonManifest> addons,
        string packsDirectory,
        string instanceRoot,
        string addonType,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(packsDirectory))
        {
            return;
        }

        foreach (string manifestPath in Directory.EnumerateFiles(packsDirectory, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BedrockAddonManifest addon = await ReadBedrockPackManifestAsync(manifestPath, instanceRoot, addonType, cancellationToken)
                .ConfigureAwait(false);

            if (!addons.Any(existing =>
                existing is BedrockAddonManifest bedrock &&
                !string.IsNullOrWhiteSpace(bedrock.Uuid) &&
                bedrock.Uuid.Equals(addon.Uuid, StringComparison.OrdinalIgnoreCase)))
            {
                addons.Add(addon);
            }
        }
    }

    private static async Task<BedrockAddonManifest> ReadBedrockPackManifestAsync(
        string manifestPath,
        string instanceRoot,
        string addonType,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(manifestPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = StreamBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement header = document.RootElement.TryGetProperty("header", out JsonElement value)
            ? value
            : document.RootElement;

        string packDirectory = Path.GetDirectoryName(manifestPath) ?? instanceRoot;
        return new BedrockAddonManifest
        {
            Name = ReadString(header, "name") ?? Path.GetFileName(packDirectory),
            Type = addonType,
            Provider = "Local",
            Uuid = ReadString(header, "uuid"),
            Version = ReadVersion(header),
            RelativePath = ToPortableRelativePath(instanceRoot, packDirectory)
        };
    }

    private async IAsyncEnumerable<ExportFile> EnumerateExportFilesAsync(
        string instanceRoot,
        bool isJava,
        bool includeWorlds,
        List<string> skippedFiles,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (ExportFile file in EnumerateExportFiles(instanceRoot, isJava, includeWorlds, skippedFiles, cancellationToken))
        {
            yield return file;
            await Task.Yield();
        }
    }

    private static IEnumerable<ExportFile> EnumerateExportFiles(
        string instanceRoot,
        bool isJava,
        bool includeWorlds,
        List<string> skippedFiles,
        CancellationToken cancellationToken)
    {
        string[] rootFiles = isJava ? JavaRootFiles : BedrockRootFiles;
        foreach (string fileName in rootFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(instanceRoot, fileName);
            if (File.Exists(path) && ShouldIncludeFile(path, isJava))
            {
                yield return CreateExportFile(instanceRoot, path);
            }
        }

        string[] directories = isJava ? JavaDirectories : BedrockDirectories;
        foreach (string directoryName in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!includeWorlds && IsWorldDirectory(directoryName, isJava))
            {
                continue;
            }

            string directory = Path.Combine(instanceRoot, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(instanceRoot, file);

                if (ShouldSkipFile(file, isJava))
                {
                    skippedFiles.Add(ToPortableRelativePath(relativePath));
                    continue;
                }

                yield return CreateExportFile(instanceRoot, file);
            }
        }
    }

    private long EstimateExportBytes(
        string instanceRoot,
        bool isJava,
        bool includeWorlds,
        List<string> skippedFiles,
        CancellationToken cancellationToken)
    {
        long totalBytes = 0;

        foreach (ExportFile file in EnumerateExportFiles(instanceRoot, isJava, includeWorlds, skippedFiles, cancellationToken))
        {
            totalBytes += file.SizeBytes;
        }

        return totalBytes;
    }

    private static async Task AddJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(ToZipEntryName(entryName), CompressionLevel.Fastest);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            InstanceExportManifest.CreateJsonOptions(),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddMetadataEntryAsync(
        ZipArchive archive,
        InstanceMetadata metadata,
        string instanceRoot,
        CancellationToken cancellationToken)
    {
        InstanceMetadata portableMetadata = await CreatePortableMetadataSnapshotAsync(metadata, instanceRoot, cancellationToken)
            .ConfigureAwait(false);
        await AddJsonEntryAsync(archive, ExportedMetadataFileName, portableMetadata, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InstanceMetadata> CreatePortableMetadataSnapshotAsync(
        InstanceMetadata requestMetadata,
        string instanceRoot,
        CancellationToken cancellationToken)
    {
        InstanceMetadata source = requestMetadata;
        string metadataPath = Path.Combine(instanceRoot, SourceMetadataFileName);

        if (File.Exists(metadataPath))
        {
            try
            {
                await using var stream = new FileStream(metadataPath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite,
                    BufferSize = StreamBufferSize,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

                source = await JsonSerializer.DeserializeAsync<InstanceMetadata>(
                        stream,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                    ?? requestMetadata;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                source = requestMetadata;
            }
        }

        InstanceMetadata snapshot = CloneMetadata(source);
        snapshot.Id = Guid.Empty;
        snapshot.LastPlayedAt = null;
        snapshot.LastBackupTime = null;
        snapshot.CustomBackupDirectory = null;
        return snapshot;
    }

    private static InstanceMetadata CloneMetadata(InstanceMetadata metadata)
    {
        string json = JsonSerializer.Serialize(metadata);
        return JsonSerializer.Deserialize<InstanceMetadata>(json) ?? new InstanceMetadata();
    }

    private static async Task AddIconEntryAsync(ZipArchive archive, string instanceRoot, CancellationToken cancellationToken)
    {
        string iconPath = Path.Combine(instanceRoot, ServerIconFileName);
        if (File.Exists(iconPath))
        {
            await AddFileEntryAsync(archive, iconPath, "meta/icon.png", cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AddFileEntryAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(ToZipEntryName(entryName), CompressionLevel.Fastest);

        await using Stream entryStream = entry.Open();
        await using var sourceStream = new FileStream(sourcePath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = StreamBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        await sourceStream.CopyToAsync(entryStream, StreamBufferSize, cancellationToken).ConfigureAwait(false);
    }

    private static ExportFile CreateExportFile(string instanceRoot, string fullPath)
    {
        var info = new FileInfo(fullPath);
        return new ExportFile(fullPath, ToPortableRelativePath(instanceRoot, fullPath), info.Length);
    }

    private static bool ShouldIncludeFile(string fullPath, bool isJava) => !ShouldSkipFile(fullPath, isJava);

    private static bool ShouldSkipFile(string fullPath, bool isJava)
    {
        string fileName = Path.GetFileName(fullPath);
        if (SkippedFileNames.Contains(fileName))
        {
            return true;
        }

        if (isJava)
        {
            return fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".jar.disabled-by-pocketmc", StringComparison.OrdinalIgnoreCase);
        }

        string extension = Path.GetExtension(fileName);
        return BedrockBinaryExtensions.Contains(extension);
    }

    private static bool IsWorldDirectory(string directoryName, bool isJava)
    {
        if (!isJava)
        {
            return directoryName.Equals("worlds", StringComparison.OrdinalIgnoreCase);
        }

        return directoryName.Equals("world", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("world_nether", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("world_the_end", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateInstanceRoot(string instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            throw new ArgumentException("Instance path is required.", nameof(instancePath));
        }

        string root = Path.GetFullPath(instancePath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Instance path '{root}' does not exist.");
        }

        return root;
    }

    private static string ValidateDestinationPath(string destinationZipPath, string instanceRoot)
    {
        if (string.IsNullOrWhiteSpace(destinationZipPath))
        {
            throw new ArgumentException("Destination ZIP path is required.", nameof(destinationZipPath));
        }

        string destination = Path.GetFullPath(destinationZipPath);
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Instance exports must use a .zip file extension.");
        }

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidDataException("Destination ZIP path must include a directory.");
        }

        string containedProbe = Path.GetRelativePath(instanceRoot, destination);
        if (!containedProbe.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(containedProbe))
        {
            throw new InvalidOperationException("Export ZIP cannot be written inside the instance directory.");
        }

        return destination;
    }

    private static void ReplaceDestination(string tempZipPath, string destinationZipPath)
    {
        if (File.Exists(destinationZipPath))
        {
            string backupPath = $"{destinationZipPath}.{Guid.NewGuid():N}.bak";
            File.Replace(tempZipPath, destinationZipPath, backupPath, ignoreMetadataErrors: true);
            TryDeleteFile(backupPath);
            return;
        }

        File.Move(tempZipPath, destinationZipPath);
    }

    private static string NormalizeServerType(string? serverType, bool isJava)
    {
        if (string.IsNullOrWhiteSpace(serverType))
        {
            return isJava ? "Vanilla" : "BDS";
        }

        if (!isJava && serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            return "BDS";
        }

        return serverType.Trim();
    }

    private static string ResolveJavaAddonType(InstanceMetadata metadata, string instanceRoot, string fileName)
    {
        if (File.Exists(Path.Combine(instanceRoot, "plugins", fileName)))
        {
            return InstanceAddonTypes.Plugin;
        }

        if (File.Exists(Path.Combine(instanceRoot, "mods", fileName)))
        {
            return InstanceAddonTypes.Mod;
        }

        return metadata.Compatibility.SupportsPlugins ? InstanceAddonTypes.Plugin : InstanceAddonTypes.Mod;
    }

    private static string ResolveBedrockAddonType(string instanceRoot, string fileName)
    {
        if (File.Exists(Path.Combine(instanceRoot, "resource_packs", fileName)))
        {
            return InstanceAddonTypes.ResourcePack;
        }

        return InstanceAddonTypes.BehaviorPack;
    }

    private static string? ResolveAddonRelativePath(string instanceRoot, string addonType, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string directoryName = addonType switch
        {
            InstanceAddonTypes.Plugin => "plugins",
            InstanceAddonTypes.Mod => "mods",
            InstanceAddonTypes.ResourcePack => "resource_packs",
            _ => "behavior_packs"
        };

        string fullPath = Path.Combine(instanceRoot, directoryName, fileName);
        return File.Exists(fullPath) || Directory.Exists(fullPath)
            ? ToPortableRelativePath(instanceRoot, fullPath)
            : null;
    }

    private static bool LooksLikeJavaAddon(string fileName) =>
        fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBedrockAddon(string fileName) =>
        fileName.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".mcaddon", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static string? FormatHash(string? hashType, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(hashType) || hash.Contains('-', StringComparison.Ordinal))
        {
            return hash;
        }

        return $"{hashType}-{hash}";
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "Unknown";
    }

    private static string GetAddonIdentity(InstanceAddonManifest addon) =>
        addon switch
        {
            JavaAddonManifest java => FirstNonEmpty(java.ProjectId, java.FileName, java.RelativePath, java.Name),
            BedrockAddonManifest bedrock => FirstNonEmpty(bedrock.Uuid, bedrock.FileName, bedrock.RelativePath, bedrock.Name),
            _ => FirstNonEmpty(addon.FileName, addon.RelativePath, addon.Name)
        };

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static string? ReadVersion(JsonElement element)
    {
        if (!element.TryGetProperty("version", out JsonElement version) ||
            version.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (version.ValueKind == JsonValueKind.Array)
        {
            return string.Join(".", version.EnumerateArray().Select(ReadVersionPart));
        }

        return version.ValueKind == JsonValueKind.String ? version.GetString() : version.GetRawText();
    }

    private static string ReadVersionPart(JsonElement part) =>
        part.ValueKind switch
        {
            JsonValueKind.Number when part.TryGetInt32(out int value) => value.ToString(),
            JsonValueKind.String => part.GetString() ?? "0",
            _ => part.GetRawText()
        };

    private static string ToPortableRelativePath(string root, string fullPath) =>
        ToPortableRelativePath(Path.GetRelativePath(root, fullPath));

    private static string ToPortableRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private static string ToZipEntryName(string path) => path.Replace('\\', '/').TrimStart('/');

    private static double CalculateProgress(long copiedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        return Math.Clamp(copiedBytes * 100d / totalBytes, 0, 99);
    }

    private static void Report(
        IProgress<InstanceTransferProgress>? progress,
        string step,
        double overallProgress,
        string? currentItem = null)
    {
        progress?.Report(new InstanceTransferProgress
        {
            CurrentStep = step,
            OverallProgress = overallProgress,
            CurrentItem = currentItem
        });
    }

    private static string GetPocketMcVersion()
    {
        Assembly assembly = typeof(InstanceExportService).Assembly;
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
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

    private readonly record struct ExportFile(string FullPath, string RelativePath, long SizeBytes);
}
