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
    public void SanitizeConsoleLine_DoesNotRedactMinecraftTimestamp()
    {
        string sanitized = LogSanitizer.SanitizeConsoleLine(
            "[19:31:50 INFO]: Starting minecraft server version 1.21.11");

        Assert.Contains("[19:31:50 INFO]", sanitized);
        Assert.DoesNotContain("[REDACTED_IP] INFO", sanitized);
    }

    [Fact]
    public void SanitizeConsoleLine_DoesNotRedactWindowsFileUriDriveLetter()
    {
        string sanitized = LogSanitizer.SanitizeConsoleLine(
            "file:/C:/Users/Sahaj33/Documents/PocketMC/server.jar");

        Assert.Contains("file:/C:/Users", sanitized);
        Assert.DoesNotContain("file:/[REDACTED_IP]/Users", sanitized);
    }

    [Fact]
    public void SanitizeConsoleLine_StripsAnsiEscapeSequences()
    {
        string sanitized = LogSanitizer.SanitizeConsoleLine(
            "\u001b[33;1m[\u001b[32;22mSkinsRestorer\u001b[33;1m]\u001b[0;39m Running");

        Assert.Contains("[SkinsRestorer] Running", sanitized);
        Assert.DoesNotContain("[33;1m", sanitized);
        Assert.DoesNotContain("[32;22m", sanitized);
        Assert.DoesNotContain("[0;39m", sanitized);
    }

    [Fact]
    public void SanitizeConsoleLine_StripsOrphanAnsiSgrSequences()
    {
        string sanitized = LogSanitizer.SanitizeConsoleLine(
            "[33;1m[[32;22mSkinsRestorer[33;1m] [0;39mRunning");

        Assert.Contains("[SkinsRestorer] Running", sanitized);
        Assert.DoesNotContain("[33;1m", sanitized);
        Assert.DoesNotContain("[32;22m", sanitized);
        Assert.DoesNotContain("[0;39m", sanitized);
    }

    [Fact]
    public void SanitizeConsoleLine_StillRedactsRealIpv6()
    {
        string sanitized = LogSanitizer.SanitizeConsoleLine(
            "Client connected from 2001:db8::1");

        Assert.Contains("[REDACTED_IP]", sanitized);
        Assert.DoesNotContain("2001:db8::1", sanitized);
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


