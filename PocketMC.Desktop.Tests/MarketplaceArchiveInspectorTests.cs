using System.IO.Compression;
using System.Text;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Tests;

public sealed class MarketplaceArchiveInspectorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.ArchiveInspector", Guid.NewGuid().ToString("N"));

    public MarketplaceArchiveInspectorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void InspectServerCompatibilityWarnings_WarnsForFabricClientOnlyEnvironment()
    {
        string jarPath = Path.Combine(_tempDirectory, "client-only.jar");
        using (ZipArchive archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("fabric.mod.json");
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write("""{ "schemaVersion": 1, "id": "clientmod", "environment": "client" }""");
        }

        IReadOnlyList<string> warnings = MarketplaceArchiveInspector.InspectServerCompatibilityWarnings(jarPath);

        Assert.Contains(warnings, warning => warning.Contains("client only mod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectServerCompatibilityWarnings_WarnsForQuiltClientOnlyEnvironment()
    {
        string jarPath = Path.Combine(_tempDirectory, "quilt-client-only.jar");
        using (ZipArchive archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("quilt.mod.json");
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write("""{ "schemaVersion": 1, "quilt_loader": { "id": "clientmod", "intermediate_mappings": "yarn" }, "environment": "client" }""");
        }

        IReadOnlyList<string> warnings = MarketplaceArchiveInspector.InspectServerCompatibilityWarnings(jarPath);

        Assert.Contains(warnings, warning => warning.Contains("client only mod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectServerCompatibilityWarnings_WarnsForForgeClientSideOnly()
    {
        string jarPath = Path.Combine(_tempDirectory, "forge-client.jar");
        using (ZipArchive archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("META-INF/mods.toml");
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write("""
            modLoader="javafml"
            loaderVersion="[45,)"
            clientSideOnly=true
            """);
        }

        IReadOnlyList<string> warnings = MarketplaceArchiveInspector.InspectServerCompatibilityWarnings(jarPath);

        Assert.Contains(warnings, warning => warning.Contains("client only mod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectServerCompatibilityWarnings_WarnsForMissingPluginYml()
    {
        string jarPath = Path.Combine(_tempDirectory, "no-plugin-metadata.jar");
        using (ZipArchive archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("somefile.txt");
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write("hello");
        }

        IReadOnlyList<string> warnings = MarketplaceArchiveInspector.InspectServerCompatibilityWarnings(jarPath, isPlugin: true);

        Assert.Contains(warnings, warning => warning.Contains("Plugin metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectServerCompatibilityWarnings_UnreadableArchive_DoesNotCrash()
    {
        string badPath = Path.Combine(_tempDirectory, "corrupted.jar");
        File.WriteAllText(badPath, "definitely not a zip file");

        IReadOnlyList<string> warnings = MarketplaceArchiveInspector.InspectServerCompatibilityWarnings(badPath);

        Assert.Empty(warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
