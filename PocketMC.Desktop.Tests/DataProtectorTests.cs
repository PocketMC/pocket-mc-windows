using System.Security.Cryptography;
using System.Text;
using PocketMC.Infrastructure.Security;

namespace PocketMC.Desktop.Tests;

public sealed class DataProtectorTests
{
    [Fact]
    public void Protect_RoundTrips_WithVersionedPayload()
    {
        string protectedValue = DataProtector.Protect("secret-value");

        Assert.StartsWith("dpapi:v2:", protectedValue, StringComparison.Ordinal);
        Assert.Equal("secret-value", DataProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Unprotect_RoundTrips_LegacyStaticEntropyPayload()
    {
        string protectedValue = ProtectWithLegacyEntropy("legacy-protected-secret");

        Assert.StartsWith("dpapi:v1:", protectedValue, StringComparison.Ordinal);
        Assert.Equal("legacy-protected-secret", DataProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Unprotect_ReturnsLegacyPlaintext_WhenValueIsNotBase64()
    {
        Assert.Equal("legacy-secret", DataProtector.Unprotect("legacy-secret"));
    }

    [Fact]
    public void Unprotect_ReturnsLegacyPlaintext_WhenUnprefixedBase64CannotBeDecrypted()
    {
        string legacySecret = Convert.ToBase64String("legacy-secret"u8.ToArray());

        Assert.Equal(legacySecret, DataProtector.Unprotect(legacySecret));
    }

    [Fact]
    public void Unprotect_Throws_WhenPrefixedBase64PayloadCannotBeDecrypted()
    {
        string corruptedPayload = "dpapi:v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.Throws<CryptographicException>(() => DataProtector.Unprotect(corruptedPayload));
    }

    [Fact]
    public void Unprotect_Throws_WhenV2PrefixedBase64PayloadCannotBeDecrypted()
    {
        string corruptedPayload = "dpapi:v2:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.Throws<CryptographicException>(() => DataProtector.Unprotect(corruptedPayload));
    }

    private static string ProtectWithLegacyEntropy(string plainText)
    {
        byte[] entropy = Encoding.UTF8.GetBytes("PocketMC-LocalSettings");
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);
        return "dpapi:v1:" + Convert.ToBase64String(cipherBytes);
    }
}
