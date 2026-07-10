using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Mods
{
    public static class JavaModMetadataService
    {
        public static bool IsPluginJar(string filePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                return archive.GetEntry("plugin.yml") != null || archive.GetEntry("paper-plugin.yml") != null;
            }
            catch
            {
                return false;
            }
        }

        private static readonly ConcurrentDictionary<(string Path, long Length, DateTime LastWriteTime, string ExpectedLoader), JavaModMetadata> _cache = new();

        public static JavaModMetadata ScanJar(string filePath, string? expectedLoader = null)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                {
                    return new JavaModMetadata
                    {
                        DisplayName = CleanJarName(Path.GetFileName(filePath)),
                        FileName = Path.GetFileName(filePath),
                        LoaderType = "Unknown"
                    };
                }

                var key = (fi.FullName, fi.Length, fi.LastWriteTime, expectedLoader ?? "");
                if (_cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var metadata = ScanJarInternal(fi, expectedLoader);
                metadata.FileName = fi.Name;
                _cache[key] = metadata;
                return metadata;
            }
            catch
            {
                return new JavaModMetadata
                {
                    DisplayName = CleanJarName(Path.GetFileName(filePath)),
                    FileName = Path.GetFileName(filePath),
                    LoaderType = "Unknown"
                };
            }
        }


        private static JavaModMetadata ScanJarInternal(FileInfo fi, string? expectedLoader)
        {
            var metadata = new JavaModMetadata();
            metadata.DisplayName = CleanJarName(fi.Name);

            try
            {
                using var archive = ZipFile.OpenRead(fi.FullName);

                bool isFabricExpected = string.Equals(expectedLoader, "Fabric", StringComparison.OrdinalIgnoreCase);
                bool isForgeExpected = string.Equals(expectedLoader, "Forge", StringComparison.OrdinalIgnoreCase);
                bool isNeoForgeExpected = string.Equals(expectedLoader, "NeoForge", StringComparison.OrdinalIgnoreCase);
                bool isQuiltExpected = string.Equals(expectedLoader, "Quilt", StringComparison.OrdinalIgnoreCase);

                var quiltEntry = archive.GetEntry("quilt.mod.json");
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                var neoforgeEntry = archive.GetEntry("META-INF/neoforge.mods.toml");
                var forgeEntry = archive.GetEntry("META-INF/mods.toml");
                var pluginEntry = archive.GetEntry("plugin.yml") ?? archive.GetEntry("paper-plugin.yml");
                metadata.HasPluginMetadata = pluginEntry != null;

                // 1. Try expected loader first to support multi-loader jars correctly
                if (isFabricExpected && fabricEntry != null)
                {
                    metadata.LoaderType = "Fabric";
                    ParseFabricMetadata(archive, fabricEntry, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                if (isNeoForgeExpected)
                {
                    if (neoforgeEntry != null)
                    {
                        metadata.LoaderType = "NeoForge";
                        ParseForgeMetadata(archive, neoforgeEntry, metadata);
                        metadata.SanitizeDependencies();
                        return metadata;
                    }
                    if (forgeEntry != null)
                    {
                        metadata.LoaderType = "Forge";
                        ParseForgeMetadata(archive, forgeEntry, metadata);
                        metadata.SanitizeDependencies();
                        return metadata;
                    }
                }

                if (isForgeExpected && forgeEntry != null)
                {
                    metadata.LoaderType = "Forge";
                    ParseForgeMetadata(archive, forgeEntry, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                if (isQuiltExpected && quiltEntry != null)
                {
                    metadata.LoaderType = "Quilt";
                    ParseQuiltMetadata(archive, quiltEntry, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                // 2. Fallback to standard check order
                if (quiltEntry != null)
                {
                    metadata.LoaderType = "Quilt";
                    ParseQuiltMetadata(archive, quiltEntry, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                if (fabricEntry != null)
                {
                    metadata.LoaderType = "Fabric";
                    ParseFabricMetadata(archive, fabricEntry, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                if (neoforgeEntry != null || forgeEntry != null)
                {
                    var entryToUse = neoforgeEntry ?? forgeEntry;
                    metadata.LoaderType = neoforgeEntry != null ? "NeoForge" : "Forge";
                    ParseForgeMetadata(archive, entryToUse!, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                // 4. Old Forge mcmod.info
                var mcmodEntry = archive.GetEntry("mcmod.info");
                if (mcmodEntry != null)
                {
                    metadata.LoaderType = "Forge";
                    ParseMcModInfoMetadata(archive, mcmodEntry, metadata);
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                // 5. Bukkit/Paper Plugin
                if (pluginEntry != null)
                {
                    metadata.LoaderType = "Plugin";
                    ParsePluginMetadata(archive, pluginEntry, metadata);

                    bool isInModsFolder = fi.FullName.Replace('\\', '/').Split('/').Contains("mods");
                    if (isInModsFolder)
                    {
                        metadata.IsPluginInModsFolder = true;
                    }
                    metadata.SanitizeDependencies();
                    return metadata;
                }

                // 6. Unknown
                metadata.LoaderType = "Unknown";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                metadata.LoaderType = "Unknown";
            }

            return metadata;
        }

        private static void ParseFabricMetadata(ZipArchive archive, ZipArchiveEntry entry, JavaModMetadata metadata)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    metadata.ModId = idProp.GetString() ?? "";
                }
                if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                {
                    metadata.DisplayName = nameProp.GetString() ?? metadata.DisplayName;
                }
                if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == JsonValueKind.String)
                {
                    metadata.Version = versionProp.GetString();
                }
                if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                {
                    metadata.Description = descProp.GetString();
                }
                metadata.SideSupport = ModSideSupport.ClientAndServer;
                metadata.SideLabel = "Client + Server";

                if (root.TryGetProperty("environment", out var envProp) && envProp.ValueKind == JsonValueKind.String)
                {
                    string env = envProp.GetString() ?? "";
                    if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.SideSupport = ModSideSupport.ClientOnly;
                        metadata.SideLabel = "Client-only";
                    }
                    else if (env.Equals("server", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.SideSupport = ModSideSupport.ServerOnly;
                        metadata.SideLabel = "Server-only";
                    }
                    else if (env.Equals("*", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.SideSupport = ModSideSupport.ClientAndServer;
                        metadata.SideLabel = "Client + Server";
                    }
                }

                if (root.TryGetProperty("depends", out var dependsProp) && dependsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in dependsProp.EnumerateObject())
                    {
                        var depName = prop.Name;
                        if (depName == "minecraft" && prop.Value.ValueKind == JsonValueKind.String)
                        {
                            metadata.RequiredMinecraftVersion = prop.Value.GetString();
                        }
                        else if (depName == "fabricloader" && prop.Value.ValueKind == JsonValueKind.String)
                        {
                            metadata.RequiredLoaderVersion = prop.Value.GetString();
                        }
                        else if (depName != "java" && depName != "fabric")
                        {
                            metadata.RequiredDependencies.Add(depName);
                        }
                    }
                }

                if (root.TryGetProperty("recommends", out var recommendsProp) && recommendsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in recommendsProp.EnumerateObject())
                    {
                        metadata.OptionalDependencies.Add(prop.Name);
                    }
                }

                if (root.TryGetProperty("suggests", out var suggestsProp) && suggestsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in suggestsProp.EnumerateObject())
                    {
                        // Avoid duplicates if a mod is in both recommends and suggests for some reason
                        if (!metadata.OptionalDependencies.Contains(prop.Name))
                        {
                            metadata.OptionalDependencies.Add(prop.Name);
                        }
                    }
                }

                if (root.TryGetProperty("icon", out var iconProp))
                {
                    string? iconPath = null;
                    if (iconProp.ValueKind == JsonValueKind.String)
                    {
                        iconPath = iconProp.GetString();
                    }
                    else if (iconProp.ValueKind == JsonValueKind.Object)
                    {
                        int maxKey = -1;
                        foreach (var prop in iconProp.EnumerateObject())
                        {
                            if (int.TryParse(prop.Name, out int size))
                            {
                                if (size > maxKey && prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    maxKey = size;
                                    iconPath = prop.Value.GetString();
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        metadata.IconEntryPath = iconPath;
                        ExtractIconBytes(archive, iconPath, metadata);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private static void ParseQuiltMetadata(ZipArchive archive, ZipArchiveEntry entry, JavaModMetadata metadata)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("quilt_loader", out var quiltLoader))
                {
                    if (quiltLoader.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        metadata.ModId = idProp.GetString() ?? "";
                    }
                    if (quiltLoader.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == JsonValueKind.String)
                    {
                        metadata.Version = versionProp.GetString();
                    }
                    metadata.SideSupport = ModSideSupport.ClientAndServer;
                    metadata.SideLabel = "Client + Server";

                    if (quiltLoader.TryGetProperty("environment", out var envProp) && envProp.ValueKind == JsonValueKind.String)
                    {
                        string env = envProp.GetString() ?? "";
                        if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
                        {
                            metadata.SideSupport = ModSideSupport.ClientOnly;
                            metadata.SideLabel = "Client-only";
                        }
                        else if (env.Equals("server", StringComparison.OrdinalIgnoreCase))
                        {
                            metadata.SideSupport = ModSideSupport.ServerOnly;
                            metadata.SideLabel = "Server-only";
                        }
                        else if (env.Equals("*", StringComparison.OrdinalIgnoreCase))
                        {
                            metadata.SideSupport = ModSideSupport.ClientAndServer;
                            metadata.SideLabel = "Client + Server";
                        }
                    }

                    if (quiltLoader.TryGetProperty("depends", out var dependsProp))
                    {
                        if (dependsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in dependsProp.EnumerateArray())
                            {
                                if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("id", out var depIdProp) && depIdProp.ValueKind == JsonValueKind.String)
                                {
                                    metadata.RequiredDependencies.Add(depIdProp.GetString()!);
                                }
                                else if (elem.ValueKind == JsonValueKind.String)
                                {
                                    metadata.RequiredDependencies.Add(elem.GetString()!);
                                }
                            }
                        }
                        else if (dependsProp.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in dependsProp.EnumerateObject())
                            {
                                metadata.RequiredDependencies.Add(prop.Name);
                            }
                        }
                    }

                    if (quiltLoader.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        {
                            metadata.DisplayName = nameProp.GetString() ?? metadata.DisplayName;
                        }
                        if (meta.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                        {
                            metadata.Description = descProp.GetString();
                        }
                        if (meta.TryGetProperty("icon", out var iconProp))
                        {
                            string? iconPath = null;
                            if (iconProp.ValueKind == JsonValueKind.String)
                            {
                                iconPath = iconProp.GetString();
                            }
                            else if (iconProp.ValueKind == JsonValueKind.Object)
                            {
                                int maxKey = -1;
                                foreach (var prop in iconProp.EnumerateObject())
                                {
                                    if (int.TryParse(prop.Name, out int size))
                                    {
                                        if (size > maxKey && prop.Value.ValueKind == JsonValueKind.String)
                                        {
                                            maxKey = size;
                                            iconPath = prop.Value.GetString();
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(iconPath))
                            {
                                metadata.IconEntryPath = iconPath;
                                ExtractIconBytes(archive, iconPath, metadata);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private static void ParseForgeMetadata(ZipArchive archive, ZipArchiveEntry entry, JavaModMetadata metadata)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string toml = reader.ReadToEnd();

                var regexTimeout = TimeSpan.FromMilliseconds(100);

                var loaderMatch = Regex.Match(toml, @"modLoader\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                if (loaderMatch.Success)
                {
                    string loader = loaderMatch.Groups[1].Value.Trim().ToLowerInvariant();
                    if (loader.Contains("neoforge"))
                    {
                        metadata.LoaderType = "NeoForge";
                    }
                }

                var idMatches = Regex.Matches(toml, @"modId\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                foreach (Match m in idMatches)
                {
                    if (m.Success)
                    {
                        string val = m.Groups[1].Value;
                        if (string.IsNullOrEmpty(metadata.ModId))
                        {
                            metadata.ModId = val;
                        }
                        // Stop after first match so we don't accidentally grab dependency modIds!
                        break;
                    }
                }

                var nameMatch = Regex.Match(toml, @"displayName\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                if (nameMatch.Success)
                {
                    metadata.DisplayName = nameMatch.Groups[1].Value;
                }

                var verMatch = Regex.Match(toml, @"\bversion\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                if (verMatch.Success)
                {
                    string verVal = verMatch.Groups[1].Value;
                    if (verVal.Contains("$") || verVal.Contains("{"))
                    {
                        metadata.Version = null;
                    }
                    else
                    {
                        metadata.Version = verVal;
                    }
                }

                var descMatch = Regex.Match(toml, @"description\s*=\s*(?:[""']{3}([\s\S]*?)[""']{3}|[""']([^""']+)[""'])", RegexOptions.IgnoreCase, regexTimeout);
                if (descMatch.Success)
                {
                    metadata.Description = descMatch.Groups[1].Success ? descMatch.Groups[1].Value : descMatch.Groups[2].Value;
                    metadata.Description = metadata.Description?.Trim();
                }

                var logoMatch = Regex.Match(toml, @"logoFile\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                if (logoMatch.Success)
                {
                    string logo = logoMatch.Groups[1].Value;
                    metadata.IconEntryPath = logo;
                    ExtractIconBytes(archive, logo, metadata);
                }

                bool isClientOnly = false;
                bool isServerOnly = false;
                bool hasDisplayTest = false;

                string lastSection = "";
                string currentDepId = "";
                bool currentDepMandatory = true;
                bool currentDepIgnored = false;
                string currentDepVersionRange = "";
                string currentDepSide = "BOTH";

                foreach (var (line, section) in EnumerateActiveTomlLines(toml))
                {
                    bool isNewSection = line.StartsWith("[");
                    var cleanLastSection = lastSection.TrimStart('[', '"', '\'');
                    if (isNewSection)
                    {
                        if (cleanLastSection.StartsWith("dependencies", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentDepId))
                        {
                            if (currentDepId.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                            {
                                metadata.RequiredMinecraftVersion = currentDepVersionRange;
                                if (currentDepSide == "CLIENT") isClientOnly = true;
                                if (currentDepSide == "SERVER") isServerOnly = true;
                            }
                            else if (currentDepId.Equals("forge", StringComparison.OrdinalIgnoreCase) || currentDepId.Equals("neoforge", StringComparison.OrdinalIgnoreCase))
                            {
                                metadata.RequiredLoaderVersion = currentDepVersionRange;
                                if (currentDepSide == "CLIENT") isClientOnly = true;
                                if (currentDepSide == "SERVER") isServerOnly = true;
                            }
                            else if (currentDepId != "java" && !currentDepIgnored)
                            {
                                if (currentDepMandatory) metadata.RequiredDependencies.Add(currentDepId);
                                else metadata.OptionalDependencies.Add(currentDepId);
                            }
                        }
                        
                        currentDepId = "";
                        currentDepMandatory = true;
                        currentDepIgnored = false;
                        currentDepVersionRange = "";
                        currentDepSide = "BOTH";
                        lastSection = section;
                    }
                    
                    var cleanSection = section.TrimStart('[', '"', '\'');
                    if (cleanSection.StartsWith("dependencies", StringComparison.OrdinalIgnoreCase))
                    {
                        var idMatch = Regex.Match(line, @"^\s*modId\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (idMatch.Success) currentDepId = idMatch.Groups[1].Value.Trim();

                        var mandatoryMatch = Regex.Match(line, @"^\s*mandatory\s*=\s*(true)", RegexOptions.IgnoreCase, regexTimeout);
                        var typeMatch = Regex.Match(line, @"^\s*type\s*=\s*[""']required[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (mandatoryMatch.Success || typeMatch.Success) { currentDepMandatory = true; currentDepIgnored = false; }
                        
                        var mandatoryFalseMatch = Regex.Match(line, @"^\s*mandatory\s*=\s*(false)", RegexOptions.IgnoreCase, regexTimeout);
                        var typeOptionalMatch = Regex.Match(line, @"^\s*type\s*=\s*[""']optional[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (mandatoryFalseMatch.Success || typeOptionalMatch.Success) { currentDepMandatory = false; currentDepIgnored = false; }

                        var typeIncompatibleMatch = Regex.Match(line, @"^\s*type\s*=\s*[""'](incompatible|discouraged)[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (typeIncompatibleMatch.Success) { currentDepIgnored = true; }

                        var versionRangeMatch = Regex.Match(line, @"^\s*versionRange\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (versionRangeMatch.Success) currentDepVersionRange = versionRangeMatch.Groups[1].Value.Trim();

                        var sideMatch = Regex.Match(line, @"^\s*side\s*=\s*[""'](CLIENT|SERVER|BOTH)[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (sideMatch.Success) currentDepSide = sideMatch.Groups[1].Value.Trim().ToUpperInvariant();
                    }
                    else if (cleanSection.StartsWith("mods.", StringComparison.OrdinalIgnoreCase) || cleanSection.StartsWith("mods", StringComparison.OrdinalIgnoreCase))
                    {
                        var idMatch = Regex.Match(line, @"^\s*modId\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                        if (idMatch.Success && string.IsNullOrEmpty(metadata.ModId)) metadata.ModId = idMatch.Groups[1].Value.Trim();
                    }
                    
                    var clientOnlyMatch = Regex.Match(line, @"^\s*clientSideOnly\s*=\s*true", RegexOptions.IgnoreCase, regexTimeout);
                    if (clientOnlyMatch.Success) isClientOnly = true;

                    var displayTestMatch = Regex.Match(line, @"^\s*displayTest\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, regexTimeout);
                    if (displayTestMatch.Success)
                    {
                        var testVal = displayTestMatch.Groups[1].Value.Trim().ToUpperInvariant();
                        if (testVal == "IGNORE_SERVER_VERSION" || testVal == "NONE" || testVal == "IGNORE_ALL_VERSION") hasDisplayTest = true;
                    }
                }
                
                // Flush the last dependency if needed
                var finalCleanLastSection = lastSection.TrimStart('[', '"', '\'');
                if (finalCleanLastSection.StartsWith("dependencies", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentDepId))
                {
                    if (currentDepId.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.RequiredMinecraftVersion = currentDepVersionRange;
                        if (currentDepSide == "CLIENT") isClientOnly = true;
                        if (currentDepSide == "SERVER") isServerOnly = true;
                    }
                    else if (currentDepId.Equals("forge", StringComparison.OrdinalIgnoreCase) || currentDepId.Equals("neoforge", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.RequiredLoaderVersion = currentDepVersionRange;
                        if (currentDepSide == "CLIENT") isClientOnly = true;
                        if (currentDepSide == "SERVER") isServerOnly = true;
                    }
                    else if (currentDepId != "java" && !currentDepIgnored)
                    {
                        if (currentDepMandatory) metadata.RequiredDependencies.Add(currentDepId);
                        else metadata.OptionalDependencies.Add(currentDepId);
                    }
                }

                if (isClientOnly)
                {
                    metadata.SideSupport = ModSideSupport.ClientOnly;
                    metadata.SideLabel = "Client-only";
                }
                else if (isServerOnly)
                {
                    metadata.SideSupport = ModSideSupport.ServerOnly;
                }
                else if (hasDisplayTest)
                {
                    metadata.SideSupport = ModSideSupport.OptionalOnServer;
                    metadata.SideLabel = "Optional on server";
                }
                else
                {
                    metadata.SideSupport = ModSideSupport.Unknown;
                    metadata.SideLabel = "Unknown";
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private static void ParseMcModInfoMetadata(ZipArchive archive, ZipArchiveEntry entry, JavaModMetadata metadata)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();

                using var doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                JsonElement modObj = default;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() > 0)
                    {
                        modObj = root[0];
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("modList", out var listProp) && listProp.ValueKind == JsonValueKind.Array)
                    {
                        if (listProp.GetArrayLength() > 0)
                        {
                            modObj = listProp[0];
                        }
                    }
                    else
                    {
                        modObj = root;
                    }
                }

                if (modObj.ValueKind == JsonValueKind.Object)
                {
                    if (modObj.TryGetProperty("modid", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        metadata.ModId = idProp.GetString() ?? "";
                    }
                    if (modObj.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        metadata.DisplayName = nameProp.GetString() ?? metadata.DisplayName;
                    }
                    if (modObj.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == JsonValueKind.String)
                    {
                        metadata.Version = versionProp.GetString();
                    }
                    if (modObj.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                    {
                        metadata.Description = descProp.GetString();
                    }
                    if (modObj.TryGetProperty("logoFile", out var logoProp) && logoProp.ValueKind == JsonValueKind.String)
                    {
                        string logo = logoProp.GetString() ?? "";
                        metadata.IconEntryPath = logo;
                        ExtractIconBytes(archive, logo, metadata);
                    }
                    if (modObj.TryGetProperty("dependencies", out var depsProp) && depsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dep in depsProp.EnumerateArray())
                        {
                            if (dep.ValueKind == JsonValueKind.String)
                            {
                                metadata.RequiredDependencies.Add(dep.GetString()!);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private static void ParsePluginMetadata(ZipArchive archive, ZipArchiveEntry entry, JavaModMetadata metadata)
        {
            metadata.SideSupport = ModSideSupport.ServerOnly;
            metadata.SideLabel = "Server-only";

            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string yaml = reader.ReadToEnd();

                var regexTimeout = TimeSpan.FromMilliseconds(100);

                var nameMatch = Regex.Match(yaml, @"^name:\s*['""]?([^\s'""]+)['""]?", RegexOptions.Multiline | RegexOptions.IgnoreCase, regexTimeout);
                if (nameMatch.Success)
                {
                    metadata.DisplayName = nameMatch.Groups[1].Value;
                    metadata.ModId = nameMatch.Groups[1].Value;
                }

                var verMatch = Regex.Match(yaml, @"^version:\s*['""]?([^\s'""]+)['""]?", RegexOptions.Multiline | RegexOptions.IgnoreCase, regexTimeout);
                if (verMatch.Success)
                {
                    metadata.Version = verMatch.Groups[1].Value;
                }

                var descMatch = Regex.Match(yaml, @"^description:\s*['""]?([^\r\n'""]+)['""]?", RegexOptions.Multiline | RegexOptions.IgnoreCase, regexTimeout);
                if (descMatch.Success)
                {
                    metadata.Description = descMatch.Groups[1].Value.Trim();
                }

                var apiVerMatch = Regex.Match(yaml, @"^api-version:\s*['""]?([^\r\n'""]+)['""]?", RegexOptions.Multiline | RegexOptions.IgnoreCase, regexTimeout);
                if (apiVerMatch.Success)
                {
                    metadata.ApiVersion = apiVerMatch.Groups[1].Value.Trim();
                }

                var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                string? currentList = null;
                foreach(var line in lines)
                {
                    if (Regex.IsMatch(line, @"^\s*depend:\s*$")) { currentList = "depend"; continue; }
                    if (Regex.IsMatch(line, @"^\s*softdepend:\s*$")) { currentList = "softdepend"; continue; }
                    
                    var listMatch = Regex.Match(line, @"^\s*-\s*['""]?([^'""]+)['""]?");
                    if (listMatch.Success && currentList != null)
                    {
                        string dep = listMatch.Groups[1].Value.Trim();
                        if (currentList == "depend") metadata.RequiredDependencies.Add(dep);
                        else metadata.OptionalDependencies.Add(dep);
                        continue;
                    }

                    var inlineDepend = Regex.Match(line, @"^\s*depend:\s*\[([^\]]+)\]");
                    if (inlineDepend.Success) {
                        foreach(var d in inlineDepend.Groups[1].Value.Split(',')) metadata.RequiredDependencies.Add(d.Trim(' ', '\'', '"'));
                        currentList = null;
                        continue;
                    }
                    var inlineSoftDepend = Regex.Match(line, @"^\s*softdepend:\s*\[([^\]]+)\]");
                    if (inlineSoftDepend.Success) {
                        foreach(var d in inlineSoftDepend.Groups[1].Value.Split(',')) metadata.OptionalDependencies.Add(d.Trim(' ', '\'', '"'));
                        currentList = null;
                        continue;
                    }

                    if (Regex.IsMatch(line, @"^\s*[a-zA-Z0-9_-]+:")) {
                        currentList = null;
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private static void ExtractIconBytes(ZipArchive archive, string iconPath, JavaModMetadata metadata)
        {
            if (string.IsNullOrEmpty(iconPath)) return;

            if (iconPath.Contains("..") || Path.IsPathRooted(iconPath))
            {
                return;
            }

            string normPath = iconPath.Replace('\\', '/').TrimStart('/');

            var iconEntry = archive.GetEntry(normPath);
            if (iconEntry == null)
            {
                iconEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(normPath, StringComparison.OrdinalIgnoreCase));
            }

            if (iconEntry != null)
            {
                if (iconEntry.Length > 1024 * 1024)
                {
                    return;
                }

                try
                {
                    using var stream = iconEntry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    metadata.IconBytes = ms.ToArray();
                }
                catch
                {
                    // Ignore extraction failures
                }
            }
        }

        private static string CleanJarName(string fileName)
        {
            string withoutExt = Path.GetFileNameWithoutExtension(fileName);
            string cleaned = withoutExt.Replace('-', ' ').Replace('_', ' ');
            cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return cleaned.Trim();
        }

        /// <summary>
        /// Returns true when the TOML content contains a bare key <paramref name="key"/>
        /// whose value is literally <c>true</c> (case-insensitive), ignoring commented
        /// lines, triple-quoted multi-line string bodies, and occurrences inside
        /// quoted string values.
        /// </summary>
        private static bool IsActiveTomlKeyTrue(string toml, string key)
        {
            var regexTimeout = TimeSpan.FromMilliseconds(100);
            var pattern = $@"^\s*{Regex.Escape(key)}\s*=\s*true\s*$";
            foreach (var (line, section) in EnumerateActiveTomlLines(toml))
            {
                if (section.StartsWith("[dependencies", StringComparison.OrdinalIgnoreCase) ||
                    section.StartsWith("[[dependencies", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase, regexTimeout))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true when the TOML content contains a bare key <paramref name="key"/>
        /// followed by <c>=</c>, ignoring commented lines, multi-line string bodies,
        /// and occurrences inside quoted string values.
        /// </summary>
        private static bool HasActiveTomlKey(string toml, string key)
        {
            var regexTimeout = TimeSpan.FromMilliseconds(100);
            var pattern = $@"^\s*{Regex.Escape(key)}\s*=";
            foreach (var (line, section) in EnumerateActiveTomlLines(toml))
            {
                if (section.StartsWith("[dependencies", StringComparison.OrdinalIgnoreCase) ||
                    section.StartsWith("[[dependencies", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase, regexTimeout))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Yields only the "active" lines of a TOML document: lines that are not
        /// comments, not inside triple-quoted multi-line strings (<c>'''</c> or
        /// <c>"""</c>), and have any inline <c>#</c> comments stripped.
        /// This is intentionally conservative — it is not a full TOML parser, but
        /// it is sufficient for detecting bare key = value pairs without false
        /// positives from description blocks, comments, or string values.
        /// </summary>
        private static IEnumerable<(string Line, string Section)> EnumerateActiveTomlLines(string toml)
        {
            bool inMultiLineString = false;
            string multiLineDelimiter = "";
            string currentSection = "";

            foreach (string rawLine in toml.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');

                // If we are inside a multi-line string, look for the closing delimiter.
                if (inMultiLineString)
                {
                    if (line.Contains(multiLineDelimiter))
                    {
                        inMultiLineString = false;
                    }
                    // Either way, skip this line — it is string content, not a key = value pair.
                    continue;
                }

                string trimmed = line.TrimStart();

                // Skip full-line comments.
                if (trimmed.StartsWith('#'))
                {
                    continue;
                }

                // Detect the start of a multi-line string value (triple quotes).
                // A line like:  description = '''  or  description = """
                // The content after the opening delimiter (including this line) is string body.
                if (trimmed.Contains("'''") || trimmed.Contains("\"\"\""))
                {
                    string delimiter = trimmed.Contains("'''") ? "'''" : "\"\"\"";
                    int firstIdx = trimmed.IndexOf(delimiter, StringComparison.Ordinal);
                    int secondIdx = trimmed.IndexOf(delimiter, firstIdx + 3, StringComparison.Ordinal);

                    if (secondIdx < 0)
                    {
                        // Opening delimiter without closing on the same line — entering multi-line string.
                        inMultiLineString = true;
                        multiLineDelimiter = delimiter;
                        continue;
                    }
                    // Both delimiters on same line — this is a single-line triple-quoted string.
                    // Fall through to yield the line (the key=value itself is still valid to inspect
                    // because clientSideOnly / displayTest would never use triple-quoted values).
                }

                // Strip inline comments: anything after an unquoted '#'.
                // Simple heuristic: find '#' that is not inside a quoted value.
                string active = StripInlineComment(trimmed);

                if (!string.IsNullOrWhiteSpace(active))
                {
                    if (active.StartsWith("["))
                    {
                        currentSection = active;
                    }
                    yield return (active, currentSection);
                }
            }
        }

        /// <summary>
        /// Strips an inline comment from a TOML line by finding the first '#' that
        /// is not inside a quoted string. Returns the portion before the comment.
        /// </summary>
        private static string StripInlineComment(string line)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                }
                else if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                }
                else if (c == '#' && !inSingleQuote && !inDoubleQuote)
                {
                    return line[..i];
                }
            }

            return line;
        }
    }
}
