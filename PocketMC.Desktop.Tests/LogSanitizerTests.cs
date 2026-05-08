using PocketMC.Desktop.Features.Console;

namespace PocketMC.Desktop.Tests;

public sealed class LogSanitizerTests
{
    [Fact]
    public void SanitizeConsoleLine_RedactsIpv4Ipv6AndEmail()
    {
        string sanitized = LogSanitizer.SanitizeConsoleLine(
            "Client 192.168.1.20 / 2001:db8::1 reported user@example.com");

        Assert.Contains("[REDACTED_IP]", sanitized);
        Assert.Contains("[REDACTED_EMAIL]", sanitized);
        Assert.DoesNotContain("192.168.1.20", sanitized);
        Assert.DoesNotContain("2001:db8::1", sanitized);
        Assert.DoesNotContain("user@example.com", sanitized);
    }

    [Fact]
    public void SanitizePlayitLine_RedactsClaimUrlAndSecretValues()
    {
        string sanitized = LogSanitizer.SanitizePlayitLine(
            "Visit link to setup https://playit.gg/claim/abc-123 secret_key = \"super-secret\"");

        Assert.Contains("https://playit.gg/claim/[REDACTED]", sanitized);
        Assert.Contains("secret_key = [REDACTED]", sanitized);
        Assert.DoesNotContain("abc-123", sanitized);
        Assert.DoesNotContain("super-secret", sanitized);
    }
}
