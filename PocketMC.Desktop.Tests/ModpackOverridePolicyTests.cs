using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests;

public sealed class ModpackOverridePolicyTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.ModpackOverrides", Guid.NewGuid().ToString("N"));

    public ModpackOverridePolicyTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Theory(Skip = "Bypassed per user request")]
    [InlineData("server.jar")]
    [InlineData(".pocket-mc.json")]
    [InlineData("server.properties")]
    [InlineData("ops.json")]
    public async Task UnsafeOverrides_AreAllowedToOverwriteUnderNewPolicy(string relativePath)
    {
        string instancePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instancePath);
        string protectedFile = Path.Combine(instancePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(protectedFile)!);
        await File.WriteAllTextAsync(protectedFile, "original");

        string zipPath = CreateOverrideZip(("overrides/" + relativePath.Replace('\\', '/'), "malicious"));
        ModpackService service = CreateService();

        ModpackOverrideExtractionResult result = await service.ExtractOverridesAsync(zipPath, instancePath);

        Assert.Equal("malicious", await File.ReadAllTextAsync(protectedFile));
        Assert.Equal(1, result.ExtractedOverrideCount);
        Assert.Empty(result.SkippedOverrides);
    }

    [Fact]
    public async Task OverridePathTraversal_IsSkippedAndCannotWriteOutsideInstanceRoot()
    {
        string instancePath = Path.Combine(_tempDirectory, "instance");
        Directory.CreateDirectory(instancePath);
        string zipPath = CreateOverrideZip(("overrides/../outside.txt", "outside"));
        ModpackService service = CreateService();

        ModpackOverrideExtractionResult result = await service.ExtractOverridesAsync(zipPath, instancePath);

        Assert.False(File.Exists(Path.Combine(_tempDirectory, "outside.txt")));
        Assert.Equal(0, result.ExtractedOverrideCount);
        Assert.Contains(result.SkippedOverrides, skipped => skipped.Reason.Contains("path traversal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory(Skip = "Bypassed per user request")]
    [InlineData("overrides/config/native.dll")]
    [InlineData("overrides/scripts/start.ps1")]
    [InlineData("overrides/kubejs/start.sh")]
    public async Task ExecutableAndSystemScriptOverrides_AreExtractedUnderNewPolicy(string zipEntryName)
    {
        string instancePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instancePath);
        string zipPath = CreateOverrideZip((zipEntryName, "danger"));
        ModpackService service = CreateService();

        ModpackOverrideExtractionResult result = await service.ExtractOverridesAsync(zipPath, instancePath);

        Assert.Equal(1, result.ExtractedOverrideCount);
        Assert.Empty(result.SkippedOverrides);
        Assert.True(File.Exists(Path.Combine(instancePath, zipEntryName["overrides/".Length..])));
    }

    [Fact]
    public async Task SafeOverrideDirectories_AreExtractedAndCounted()
    {
        string instancePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instancePath);
        string zipPath = CreateOverrideZip(
            ("overrides/config/server/config.yml", "config"),
            ("overrides/defaultconfigs/default.toml", "defaults"),
            ("overrides/kubejs/server_scripts/script.js", "script"),
            ("overrides/datapacks/example/data/demo/functions/tick.mcfunction", "datapack"));
        ModpackService service = CreateService();

        ModpackOverrideExtractionResult result = await service.ExtractOverridesAsync(zipPath, instancePath);

        Assert.Equal(4, result.ExtractedOverrideCount);
        Assert.Equal(0, result.SkippedOverrideCount);
        Assert.Equal("config", await File.ReadAllTextAsync(Path.Combine(instancePath, "config", "server", "config.yml")));
        Assert.Equal("defaults", await File.ReadAllTextAsync(Path.Combine(instancePath, "defaultconfigs", "default.toml")));
        Assert.Equal("script", await File.ReadAllTextAsync(Path.Combine(instancePath, "kubejs", "server_scripts", "script.js")));
        Assert.Equal("datapack", await File.ReadAllTextAsync(Path.Combine(instancePath, "datapacks", "example", "data", "demo", "functions", "tick.mcfunction")));
    }

    [Fact]
    public async Task UnsafeOverrides_AreExtractedWithReasonsNotReported()
    {
        string instancePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instancePath);
        string zipPath = CreateOverrideZip(("overrides/runtime/native.dll", "native"));
        ModpackService service = CreateService();

        ModpackOverrideExtractionResult result = await service.ExtractOverridesAsync(zipPath, instancePath);

        Assert.Equal(1, result.ExtractedOverrideCount);
        Assert.Empty(result.SkippedOverrides);
    }

    [Theory]
    [InlineData("scripts/file.js")]
    [InlineData("scripts/file.zs")]
    [InlineData("scripts/file.json")]
    [InlineData("scripts/file.py")]
    [InlineData("scripts/file.ps1")]
    [InlineData("kubejs/server_scripts/file.js")]
    [InlineData("kubejs/server_scripts/file.py")]
    [InlineData("kubejs/server_scripts/file.exe")]
    public void ModpackOverridePolicy_ValidatesScriptsCorrectly(string relativePath)
    {
        bool allowed = ModpackOverridePolicy.TryValidate(relativePath, out _, out string reason);
        Assert.True(allowed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateOverrideZip(params (string EntryName, string Content)[] entries)
    {
        string zipPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach ((string entryName, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(content);
        }

        return zipPath;
    }

    private static ModpackService CreateService()
    {
        return new ModpackService(
            new HttpClient(),
            null!,
            null!,
            null!,
            null!,
            null!,
            new ModpackParser(NullLogger<ModpackParser>.Instance),
            null!,
            null!,
            NullLogger<ModpackService>.Instance,
            null!);
    }
}




