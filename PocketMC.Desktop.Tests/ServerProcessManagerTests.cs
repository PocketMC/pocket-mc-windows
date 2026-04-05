using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Tests;

public class ServerProcessManagerTests
{
    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 1, 20)]
    [InlineData(10, 2, 40)]
    [InlineData(10, 10, 300)]
    public void CalculateRestartDelaySeconds_UsesExponentialBackoffWithCap(int baseDelaySeconds, int attempts, int expectedDelay)
    {
        Assert.Equal(expectedDelay, ServerProcessManager.CalculateRestartDelaySeconds(baseDelaySeconds, attempts));
    }
}
