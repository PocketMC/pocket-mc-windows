using PocketMC.Desktop.Features.Diagnostics;

namespace PocketMC.Desktop.Tests;

public sealed class SupportBundleRedactorTests
{
    [Fact]
    public void Redact_RemovesCommonSecretLikeValues()
    {
        var redactor = new SupportBundleRedactor();
        string input = "api_key=abc123\naccess_token: xyz789\nAuthorization: Bearer token-value\nuser@example.com";

        string output = redactor.Redact(input);

        Assert.DoesNotContain("abc123", output);
        Assert.DoesNotContain("xyz789", output);
        Assert.DoesNotContain("token-value", output);
        Assert.DoesNotContain("user@example.com", output);
        Assert.Contains("[REDACTED", output);
    }
}
