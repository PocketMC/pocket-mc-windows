using PocketMC.Infrastructure.Backups.OAuth;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Backups;

public sealed class LoopbackOAuthReceiverTests
{
    [Theory]
    [InlineData("/callback", true)]
    [InlineData("/callback/", true)]
    [InlineData("/favicon.ico", false)]
    [InlineData("/", false)]
    [InlineData("/callback/extra", false)]
    [InlineData(null, false)]
    public void MatchesCallbackPath_OnlyAcceptsTheRegisteredCallback(string? path, bool expected)
    {
        Assert.Equal(expected, LoopbackOAuthReceiver.MatchesCallbackPath(path, "/callback"));
    }
}
