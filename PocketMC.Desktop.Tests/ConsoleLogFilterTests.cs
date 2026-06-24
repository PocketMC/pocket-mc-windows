using PocketMC.Desktop.Features.Console;

namespace PocketMC.Desktop.Tests;

public sealed class ConsoleLogFilterTests
{
    [Fact]
    public void MatchesSearch_ReturnsFalseForInvalidRegex()
    {
        bool result = ConsoleLogFilter.MatchesSearch(
            "hello world",
            "[",
            useRegex: true);

        Assert.False(result);
    }

    [Fact]
    public void MatchesSearch_UsesRequiredAndExcludedPlainTerms()
    {
        Assert.True(ConsoleLogFilter.MatchesSearch("Steve joined the game", "Steve -left", useRegex: false));
        Assert.False(ConsoleLogFilter.MatchesSearch("Steve left the game", "Steve -left", useRegex: false));
        Assert.False(ConsoleLogFilter.MatchesSearch("Alex joined the game", "Steve -left", useRegex: false));
    }

    [Fact]
    public void MatchesSearch_HandlesEmptyQueryAsMatch()
    {
        Assert.True(ConsoleLogFilter.MatchesSearch("anything", "   ", useRegex: false));
    }
}


