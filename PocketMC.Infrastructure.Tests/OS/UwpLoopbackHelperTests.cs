using PocketMC.Infrastructure.OS;
using PocketMC.Infrastructure.Tests.TestSupport.Utilities;
namespace PocketMC.Infrastructure.Tests.OS;

public sealed class UwpLoopbackHelperTests
{
    [Fact]
    public void IsExemptionPresent_UsesAsyncProcessWaitWithTimeout()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Infrastructure",
            "OS",
            "UwpLoopbackHelper.cs"));

        Assert.DoesNotContain("StandardOutput.ReadToEnd()", source);
        Assert.DoesNotContain("proc.WaitForExit();", source);
        Assert.Contains("RedirectStandardError = true", source);
        Assert.Contains("WaitForExitAsync(cts.Token)", source);
    }
}