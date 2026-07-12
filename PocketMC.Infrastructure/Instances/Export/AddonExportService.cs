using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Infrastructure.Mods;
using PocketMC.Application.Services.Mods;

namespace PocketMC.Infrastructure.Instances;

public class AddonExportService
{
    private const int StreamBufferSize = 1024 * 128;
    private readonly AddonManifestService _addonManifestService;

    public AddonExportService(AddonManifestService addonManifestService)
    {
        _addonManifestService = addonManifestService;
    }

    public async Task<IReadOnlyList<InstanceAddonManifest>> BuildAddonManifestAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        bool isJava,
        CancellationToken cancellationToken)
    {
        var addons = new List<InstanceAddonManifest>();
        AddonManifest providerManifest = await _addonManifestService.LoadManifestAsync(instanceRoot).ConfigureAwait(false);

        if (isJava)
        {
            await BuildCanonicalJavaAddonsAsync(addons, metadata, instanceRoot, providerManifest, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            AddBedrockProviderAddons(addons, instanceRoot, providerManifest);
            await AddLocalBedrockPackAddonsAsync(addons, instanceRoot, cancellationToken).ConfigureAwait(false);
        }

        return addons
            .OrderBy(addon => addon.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task BuildCanonicalJavaAddonsAsync(
        List<InstanceAddonManifest> addons,
        InstanceMetadata metadata,
        string instanceRoot,
        AddonManifest providerManifest,
        CancellationToken cancellationToken)
    {
        var entryLookup = new Dictionary<string, List<AddonManifestEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (AddonManifestEntry entry in providerManifest.Entries)
        {
            if (!LooksLikeJavaAddon(entry.FileName) && !LooksLikeDisabledJavaAddon(entry.FileName))
            {
                continue;
            }

            string key = NormalizeAddonFileName(entry.FileName);
            if (!entryLookup.TryGetValue(key, out List<AddonManifestEntry>? list))
            {
                list = new List<AddonManifestEntry>();
                entryLookup[key] = list;
            }

            list.Add(entry);
        }

        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(file);

                bool isEnabled = AddonFileNamePolicy.IsEnabledJarFileName(fileName);
                bool isDisabled = AddonFileNamePolicy.IsDisabledJarFileName(fileName);

                if (!isEnabled && !isDisabled)
                {
                    continue;
                }

                if (!processedFiles.Add(fileName))
                {
                    continue;
                }

                var fileInfo = new FileInfo(file);
                (string sha1, string sha512) = await ComputeFileHashesAsync(file, cancellationToken).ConfigureAwait(false);

                string normalizedName = NormalizeAddonFileName(fileName);
                string enabledFileName = isDisabled
                    ? AddonFileNamePolicy.GetOriginalFileNameFromDisabled(fileName)
                    : fileName;

                var matchingEntries = new List<AddonManifestEntry>();
                if (entryLookup.TryGetValue(normalizedName, out List<AddonManifestEntry>? byName))
                {
                    matchingEntries.AddRange(byName);
                }

                if (!enabledFileName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                    entryLookup.TryGetValue(enabledFileName, out List<AddonManifestEntry>? byEnabled))
                {
                    foreach (var entry in byEnabled)
                    {
                        if (!matchingEntries.Contains(entry))
                        {
                            matchingEntries.Add(entry);
                        }
                    }
                }

                List<ProviderIdentity>? providerIdentities = null;
                if (matchingEntries.Count > 0)
                {
                    providerIdentities = new List<ProviderIdentity>();
                    foreach (AddonManifestEntry entry in matchingEntries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Provider) &&
                            !entry.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
                        {
                            providerIdentities.Add(new ProviderIdentity
                            {
                                Provider = entry.Provider,
                                ProjectId = entry.ProjectId ?? string.Empty,
                                VersionId = entry.VersionId ?? string.Empty
                            });
                        }
                    }

                    if (providerIdentities.Count == 0)
                    {
                        providerIdentities = null;
                    }
                }

                AddonManifestEntry? primary = matchingEntries.FirstOrDefault(
                    e => !string.IsNullOrWhiteSpace(e.Provider) &&
                         e.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase) == false)
                    ?? matchingEntries.FirstOrDefault();

                string provider = primary != null
                    ? FirstNonEmpty(primary.Provider, "Local")
                    : "Local";
                string name = primary != null
                    ? FirstNonEmpty(primary.DisplayName, primary.ProjectTitle, Path.GetFileNameWithoutExtension(enabledFileName))
                    : Path.GetFileNameWithoutExtension(enabledFileName);

                string relativePath = ToPortableRelativePath(instanceRoot, file);

                addons.Add(new JavaAddonManifest
                {
                    Name = name,
                    Type = addonType,
                    Provider = provider,
                    FileName = enabledFileName,
                    RelativePath = relativePath,
                    Size = fileInfo.Length,
                    Sha1 = sha1,
                    Sha512 = sha512,
                    IsDisabled = isDisabled,
                    PackagedPath = ToPortableRelativePath("server", relativePath),
                    ProviderIdentities = providerIdentities,
                    ProjectId = primary != null ? EmptyToNull(primary.ProjectId) : null,
                    VersionId = primary != null ? EmptyToNull(primary.VersionId) : null,
                    Hash = primary != null ? FormatHash(primary.FileHashType, primary.FileHash) : $"sha512-{sha512}",
                    Loader = EmptyToNull(primary?.Loader ?? metadata.Compatibility.LoaderName),
                    DownloadUrl = primary != null ? EmptyToNull(primary.DownloadUrl) : null
                });
            }
        }
    }

    private static async Task<(string sha1, string sha512)> ComputeFileHashesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        string sha1 = Convert.ToHexString(SHA1.HashData(fileBytes)).ToLowerInvariant();
        string sha512 = Convert.ToHexString(SHA512.HashData(fileBytes)).ToLowerInvariant();
        return (sha1, sha512);
    }

    private static string NormalizeAddonFileName(string fileName)
    {
        if (AddonFileNamePolicy.IsDisabledJarFileName(fileName))
        {
            return AddonFileNamePolicy.GetOriginalFileNameFromDisabled(fileName);
        }

        return fileName;
    }

    private static bool LooksLikeDisabledJavaAddon(string fileName) =>
        fileName.EndsWith(".jar" + AddonFileNamePolicy.DisabledSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeJavaAddon(string fileName) =>
        fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBedrockAddon(string fileName) =>
        fileName.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".mcaddon", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

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

    private async Task AddLocalBedrockPackAddonsAsync(
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

    private async Task AddLocalBedrockPackDirectoryAsync(
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

        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using JsonDocument document = await JsonDocument.ParseAsync(stream, options, cancellationToken: cancellationToken)
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
}
