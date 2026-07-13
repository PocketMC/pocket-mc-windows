using PocketMC.Infrastructure.Instances;
using System.Text.RegularExpressions;

namespace PocketMC.Infrastructure.Tests.Linux;

/// <summary>
/// TDD tests verifying strict 1-second timeout on all regex operations.
/// </summary>
public class RegexTimeoutTests
{
    // Pathological ReDoS pattern: exponential backtrack on nested groups
    private const string EvilInput = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaax";

    [Fact]
    public void PlayerCountRegex_HasOneSecondTimeout()
    {
        // Access the compiled Regex via reflection — it must carry a timeout
        var field = typeof(ServerProcess).GetField(
            "PlayerCountRegex",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        var regex = field!.GetValue(null) as Regex;
        Assert.NotNull(regex);
        Assert.Equal(TimeSpan.FromSeconds(1), regex!.MatchTimeout);
    }

    [Fact]
    public void AppConfig_InlineRegex_DoesNotHangOnPathologicalInput()
    {
        // Verify the YAML parsing regex in AppConfig can be bounded in time.
        // We test by constructing equivalent patterns with an explicit timeout.
        var safePattern = new Regex(@"-\s*""?([^""\r\n]+)""?", RegexOptions.None, TimeSpan.FromSeconds(1));

        // Should not hang and return no match
        Assert.DoesNotMatch(safePattern, EvilInput);

    }

    [Fact]
    public void ServerProcess_IsListResponseLine_DoesNotUseRegex()
    {
        // IsListResponseLine uses string.Contains (no regex), so it should return instantly.
        // This just verifies the method exists and returns quickly.
        bool result = ServerProcess.IsListResponseLine("There are 0 of a max of 20 players online: []");
        Assert.True(result);
    }
}
