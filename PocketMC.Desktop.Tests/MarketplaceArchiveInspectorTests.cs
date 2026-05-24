using System.IO.Compression;
using System.Text;
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

        Assert.Contains(warnings, warning => warning.Contains("client-only", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
