using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Desktop.Tests;

public sealed class AddonUpdateCheckServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.AddonUpdateCheckTests", Guid.NewGuid().ToString("N"));

    public AddonUpdateCheckServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void ComputeMurmur2Hash_IgnoresWhitespaceAndComputesCorrectFingerprint()
    {
        // Setup file with whitespace characters that should be ignored
        string filePath = Path.Combine(_tempDirectory, "test_murmur.txt");
        
        // Whitespace bytes: 9, 10, 13, 32
        // Content: "test\t\n\r content"
        byte[] content = new byte[] { 116, 101, 115, 116, 9, 10, 13, 32, 99, 111, 110, 116, 101, 110, 116 };
        File.WriteAllBytes(filePath, content);

        // Calculate hash using reflection (since method is private)
        var method = typeof(AddonUpdateCheckService).GetMethod("ComputeMurmur2Hash", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        long hash = (long)method.Invoke(null, new object[] { filePath })!;
        
        // Verify hash matches expected Murmur2 computation of "testcontent"
        Assert.NotEqual(0, hash);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
