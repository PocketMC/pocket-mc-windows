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
        private static readonly ConcurrentDictionary<(string Path, long Length, DateTime LastWriteTime), JavaModMetadata> _cache = new();

        public static JavaModMetadata ScanJar(string filePath)
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
                        LoaderType = "Unknown",
                        Warnings = new List<string> { "File does not exist." }
                    };
                }

                var key = (fi.FullName, fi.Length, fi.LastWriteTime);
                if (_cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var metadata = ScanJarInternal(fi);
                metadata.FileName = fi.Name;
                _cache[key] = metadata;
                return metadata;
            }
            catch (Exception ex)
            {
                return new JavaModMetadata
                {
                    DisplayName = CleanJarName(Path.GetFileName(filePath)),
                    FileName = Path.GetFileName(filePath),
                    LoaderType = "Unknown",
                    Warnings = new List<string> { $"Failed to scan JAR: {ex.Message}" }
                };
            }
        }

        private static JavaModMetadata ScanJarInternal(FileInfo fi)
        {
            var metadata = new JavaModMetadata();
            metadata.DisplayName = CleanJarName(fi.Name);

            try
            {
                using var archive = ZipFile.OpenRead(fi.FullName);

                // 1. Quilt
                var quiltEntry = archive.GetEntry("quilt.mod.json");
                if (quiltEntry != null)
                {
                    metadata.LoaderType = "Quilt";
                    ParseQuiltMetadata(archive, quiltEntry, metadata);
                    return metadata;
                }

                // 2. Fabric
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    metadata.LoaderType = "Fabric";
                    ParseFabricMetadata(archive, fabricEntry, metadata);
                    return metadata;
                }

                // 3. Forge / NeoForge TOML
                var neoforgeEntry = archive.GetEntry("META-INF/neoforge.mods.toml");
                var forgeEntry = archive.GetEntry("META-INF/mods.toml");
                if (neoforgeEntry != null || forgeEntry != null)
                {
                    var entryToUse = neoforgeEntry ?? forgeEntry;
                    metadata.LoaderType = neoforgeEntry != null ? "NeoForge" : "Forge";
                    ParseForgeMetadata(archive, entryToUse!, metadata);
                    return metadata;
                }

                // 4. Old Forge mcmod.info
                var mcmodEntry = archive.GetEntry("mcmod.info");
                if (mcmodEntry != null)
                {
                    metadata.LoaderType = "Forge";
                    ParseMcModInfoMetadata(archive, mcmodEntry, metadata);
                    return metadata;
                }

                // 5. Bukkit/Paper Plugin
                var pluginEntry = archive.GetEntry("plugin.yml") ?? archive.GetEntry("paper-plugin.yml");
                if (pluginEntry != null)
                {
                    metadata.LoaderType = "Plugin";
                    ParsePluginMetadata(archive, pluginEntry, metadata);
                    
                    bool isInModsFolder = fi.FullName.Replace('\\', '/').Split('/').Contains("mods");
                    if (isInModsFolder)
                    {
                        metadata.IsPluginInModsFolder = true;
                        metadata.Warnings.Add("This looks like a plugin JAR, not a mod. Move it to plugins.");
                    }
                    return metadata;
                }

                // 6. Unknown
                metadata.LoaderType = "Unknown";
                metadata.Warnings.Add("Could not read mod metadata.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                metadata.LoaderType = "Unknown";
                metadata.Warnings.Add("Could not read mod metadata.");
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
                if (root.TryGetProperty("environment", out var envProp) && envProp.ValueKind == JsonValueKind.String)
                {
                    string env = envProp.GetString() ?? "";
                    if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.IsClientOnly = true;
                        metadata.Warnings.Add("Fabric metadata marks this add-on as client-only. It may crash or be ignored by a server.");
                    }
                }

                if (root.TryGetProperty("depends", out var dependsProp) && dependsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in dependsProp.EnumerateObject())
                    {
                        metadata.Dependencies.Add(prop.Name);
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
                    if (quiltLoader.TryGetProperty("environment", out var envProp) && envProp.ValueKind == JsonValueKind.String)
                    {
                        string env = envProp.GetString() ?? "";
                        if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
                        {
                            metadata.IsClientOnly = true;
                            metadata.Warnings.Add("Quilt metadata marks this add-on as client-only. It may crash or be ignored by a server.");
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
                                    metadata.Dependencies.Add(depIdProp.GetString()!);
                                }
                                else if (elem.ValueKind == JsonValueKind.String)
                                {
                                    metadata.Dependencies.Add(elem.GetString()!);
                                }
                            }
                        }
                        else if (dependsProp.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in dependsProp.EnumerateObject())
                            {
                                metadata.Dependencies.Add(prop.Name);
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
                bool first = true;
                foreach (Match m in idMatches)
                {
                    if (m.Success)
                    {
                        string val = m.Groups[1].Value;
                        if (first)
                        {
                            metadata.ModId = val;
                            first = false;
                        }
                        else
                        {
                            metadata.Dependencies.Add(val);
                        }
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

                bool isClientOnly = Regex.IsMatch(toml, @"clientSideOnly\s*=\s*true", RegexOptions.IgnoreCase, regexTimeout);
                bool isDisplayTestClient = Regex.IsMatch(toml, @"displayTest\s*=\s*['""]?(IGNORE_SERVER_VERSION|IGNORE_ALL_VERSION|NONE)['""]?", RegexOptions.IgnoreCase, regexTimeout);
                if (isClientOnly || isDisplayTestClient)
                {
                    metadata.IsClientOnly = true;
                    metadata.Warnings.Add("Forge/NeoForge metadata suggests this add-on is client-only or not required on the server.");
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
                                metadata.Dependencies.Add(dep.GetString()!);
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
    }
}
