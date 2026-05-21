namespace PocketMC.Desktop.Tests;

public sealed class DiskWriteSafetyTests
{
    [Theory]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Intelligence", "SummaryStorageService.cs" },
        "File.WriteAllText(filePath",
        "FileUtils.AtomicWriteAllText(filePath")]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Instances", "Backups", "BackupMetadata.cs" },
        "File.WriteAllText(path",
        "FileUtils.AtomicWriteAllText(path")]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Marketplace", "AddonManifestService.cs" },
        "File.WriteAllTextAsync(path",
        "FileUtils.AtomicWriteAllTextAsync(path")]
    [InlineData(
        new[] { "PocketMC.Desktop", "Features", "Tunnel", "PlayitAgentService.cs" },
        "File.WriteAllText(tomlPath",
        "FileUtils.AtomicWriteAllText(tomlPath")]
    public void PersistentStateWriters_UseAtomicFileReplacement(
        string[] sourcePath,
        string unsafeWriteCall,
        string expectedAtomicWriteCall)
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(sourcePath));

        Assert.DoesNotContain(unsafeWriteCall, source);
        Assert.Contains(expectedAtomicWriteCall, source);
    }
}
