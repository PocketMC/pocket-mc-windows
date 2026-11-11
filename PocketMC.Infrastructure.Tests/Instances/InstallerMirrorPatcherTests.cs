using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using PocketMC.Infrastructure.Instances;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances;

public sealed class InstallerMirrorPatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pocketmc-patcher-" + Guid.NewGuid().ToString("N"));

    public InstallerMirrorPatcherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void PatchesEmbeddedMavenAndMojangUrls()
    {
        string source = CreateJar("version.json", """
            {"libraries":[
              {"url":"https://libraries.minecraft.net/com/google/guava/guava-1.jar"},
              {"url":"https://maven.neoforged.net/releases/net/neoforged/bootstraplauncher-1.jar"},
              {"url":"https://mirrors.neoforged.net/releases/net/neoforged/installertools-1.jar"},
              {"url":"https://maven.minecraftforge.net/net/minecraftforge/forge-1.jar"}
            ]}
            """);
        string target = Path.Combine(_dir, "installer.mirrored.jar");

        bool patched = InstallerMirrorPatcher.TryCreatePatchedCopy(source, target, "https://mirror.example/maven/");

        Assert.True(patched);
        string json = ReadEntry(target, "version.json");
        Assert.Contains("https://mirror.example/maven/com/google/guava/guava-1.jar", json);
        Assert.Contains("https://mirror.example/maven/net/neoforged/bootstraplauncher-1.jar", json);
        Assert.Contains("https://mirror.example/maven/net/neoforged/installertools-1.jar", json);
        Assert.Contains("https://mirror.example/maven/net/minecraftforge/forge-1.jar", json);
        Assert.DoesNotContain("libraries.minecraft.net", json);
        Assert.DoesNotContain("maven.neoforged.net", json);
    }

    [Fact]
    public void CopiesBinaryEntriesVerbatim()
    {
        byte[] payload = { 0, 1, 2, 3, 250, 251 };
        string source = CreateJar(
            "version.json",
            "{\"url\":\"https://libraries.minecraft.net/com/example/lib.jar\"}",
            null,
            ("data/installer.bin", payload));
        string target = Path.Combine(_dir, "patched.jar");

        Assert.True(InstallerMirrorPatcher.TryCreatePatchedCopy(source, target, "https://mirror.example/maven"));
        Assert.Equal(payload, ReadBytes(target, "data/installer.bin"));
    }

    [Fact]
    public void ReturnsFalse_WhenJarHasNoDownloadUrls()
    {
        string source = CreateJar("version.json", "{\"id\":\"1.21.1\"}");
        string target = Path.Combine(_dir, "patched.jar");

        Assert.False(InstallerMirrorPatcher.TryCreatePatchedCopy(source, target, "https://mirror.example/maven"));
    }

    [Fact]
    public void ReturnsFalse_WhenSourceMissing()
    {
        Assert.False(InstallerMirrorPatcher.TryCreatePatchedCopy(
            Path.Combine(_dir, "missing.jar"),
            Path.Combine(_dir, "patched.jar"),
            "https://mirror.example/maven"));
    }

    private string CreateJar(string entryName, string text, byte[]? raw = null, params (string Name, byte[] Bytes)[] extra)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".jar");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, entryName, raw ?? Encoding.UTF8.GetBytes(text));
        foreach (var (name, bytes) in extra)
        {
            WriteEntry(archive, name, bytes);
        }

        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static string ReadEntry(string jar, string name)
    {
        using var archive = ZipFile.OpenRead(jar);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(string jar, string name)
    {
        using var archive = ZipFile.OpenRead(jar);
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
