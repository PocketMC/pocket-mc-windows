using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PocketMC.Infrastructure.Instances;

/// <summary>
/// Rewrites download URLs inside Forge/NeoForge installer jars so the embedded installer
/// (a plain Java process we cannot proxy) fetches libraries from a maven mirror instead
/// of blocked upstream hosts.
/// </summary>
public static class InstallerMirrorPatcher
{
    // Longest prefixes first so "maven.neoforged.net/releases" is not matched by the shorter host rule.
    private static readonly (string Upstream, string MirrorSuffix)[] HostRewrites =
    {
        ("https://maven.neoforged.net/releases/", "/"),
        ("https://mirrors.neoforged.net/releases/", "/"),
        ("https://maven.neoforged.net/", "/"),
        ("https://mirrors.neoforged.net/", "/"),
        ("https://libraries.minecraft.net/", "/"),
        ("https://maven.minecraftforge.net/", "/"),
        ("https://files.minecraftforge.net/maven/", "/"),
        ("https://repo1.maven.org/maven2/", "/"),
        ("https://repo.maven.apache.org/maven2/", "/")
    };

    /// <summary>
    /// Creates <paramref name="targetJar"/> as a copy of <paramref name="sourceJar"/> whose JSON
    /// entries point at <paramref name="mavenMirrorBase"/>. Returns false when the jar cannot be read.
    /// </summary>
    public static bool TryCreatePatchedCopy(string sourceJar, string targetJar, string mavenMirrorBase)
    {
        if (!File.Exists(sourceJar) || string.IsNullOrWhiteSpace(mavenMirrorBase))
        {
            return false;
        }

        string mirrorBase = mavenMirrorBase.TrimEnd('/');
        string tempTarget = targetJar + ".tmp";
        int replaced = 0;

        try
        {
            using (var input = File.OpenRead(sourceJar))
            using (var source = new ZipArchive(input, ZipArchiveMode.Read))
            using (var output = File.Create(tempTarget))
            using (var target = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries)
                {
                    // Only installer metadata carries download URLs; binaries are copied verbatim.
                    if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        CopyEntry(entry, target, null);
                        continue;
                    }

                    string text;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        text = reader.ReadToEnd();
                    }

                    string patched = text;
                    foreach (var (upstream, suffix) in HostRewrites)
                    {
                        patched = patched.Replace(upstream, mirrorBase + suffix, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!ReferenceEquals(patched, text) && patched != text)
                    {
                        replaced++;
                    }

                    CopyEntry(entry, target, patched);
                }
            }

            if (File.Exists(targetJar)) File.Delete(targetJar);
            File.Move(tempTarget, targetJar);
            return replaced > 0;
        }
        catch (Exception)
        {
            TryDelete(tempTarget);
            return false;
        }
    }

    private static void CopyEntry(ZipArchiveEntry entry, ZipArchive target, string? textOverride)
    {
        var created = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
        created.LastWriteTime = entry.LastWriteTime;
        using var outputStream = created.Open();

        if (textOverride != null)
        {
            using var writer = new StreamWriter(outputStream, new UTF8Encoding(false));
            writer.Write(textOverride);
            return;
        }

        using var sourceStream = entry.Open();
        sourceStream.CopyTo(outputStream);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
