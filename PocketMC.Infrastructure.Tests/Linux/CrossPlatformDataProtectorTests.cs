using PocketMC.Infrastructure.Security;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PocketMC.Infrastructure.Tests.Linux;

/// <summary>
/// TDD tests for cross-platform DataProtector.
/// Unix permission tests are gated behind OperatingSystem.IsLinux().
/// </summary>
public class CrossPlatformDataProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_ReturnsOriginalValue()
    {
        const string plaintext = "SuperSecret!123";
        var protector = new CrossPlatformDataProtector("pocketmc-test-key");

        string? cipher = protector.Protect("SuperSecret!123");
        string? result = protector.Unprotect(cipher);

        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void Protect_EmptyString_ReturnsEmpty()
    {
        var protector = new CrossPlatformDataProtector("pocketmc-test-key");
        Assert.Equal(string.Empty, protector.Protect(string.Empty));
    }

    [Fact]
    public void Protect_NullString_ReturnsNull()
    {
        var protector = new CrossPlatformDataProtector("pocketmc-test-key");
            string? cipher = protector.Protect(null!);
            Assert.Null(cipher);
    }

    [SkippableFact]
    public void KeyDirectory_OnLinux_HasMode600()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        string keyDir = Path.Combine(Path.GetTempPath(), $"pocketmc-keys-{Guid.NewGuid():N}");

        try
        {
            var protector = new CrossPlatformDataProtector("pocketmc-test-key", keyDir);
            // Force key creation
            string? cipher = protector.Protect("test");
            protector.Unprotect(cipher);


            // Check each key file has mode 600
#pragma warning disable CA1416 // Validated by SkippableFact gating on OperatingSystem.IsLinux()
            foreach (string keyFile in Directory.EnumerateFiles(keyDir))
            {
                var mode = File.GetUnixFileMode(keyFile);
                Assert.True(mode.HasFlag(UnixFileMode.UserRead), $"Key file {keyFile} must be UserRead");
                Assert.True(mode.HasFlag(UnixFileMode.UserWrite), $"Key file {keyFile} must be UserWrite");
                Assert.False(mode.HasFlag(UnixFileMode.GroupRead), $"Key file {keyFile} must NOT be GroupRead");
                Assert.False(mode.HasFlag(UnixFileMode.OtherRead), $"Key file {keyFile} must NOT be OtherRead");
            }
#pragma warning restore CA1416
        }
        finally
        {
            if (Directory.Exists(keyDir)) Directory.Delete(keyDir, recursive: true);
        }
    }
}
