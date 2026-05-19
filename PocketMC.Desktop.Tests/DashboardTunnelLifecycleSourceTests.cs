namespace PocketMC.Desktop.Tests;

public sealed class DashboardTunnelLifecycleSourceTests
{
    [Fact]
    public void DashboardStateChange_TriggersTunnelResolutionForRunningServers()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Dashboard",
            "DashboardViewModel.cs"));

        int handlerStart = source.IndexOf("private void OnInstanceStateChanged", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0);

        int nextMethod = source.IndexOf("private void OnInstanceMetricsUpdated", handlerStart, StringComparison.Ordinal);
        Assert.True(nextMethod > handlerStart);

        string handlerBody = source[handlerStart..nextMethod];
        Assert.Contains("EnsureTunnelFlowAsync", handlerBody);
    }

    [Fact]
    public void DashboardActivate_RefreshesTunnelResolutionForAlreadyRunningServers()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Dashboard",
            "DashboardViewModel.cs"));

        int activateStart = source.IndexOf("public void Activate()", StringComparison.Ordinal);
        Assert.True(activateStart >= 0);

        int deactivateStart = source.IndexOf("public void Deactivate()", activateStart, StringComparison.Ordinal);
        Assert.True(deactivateStart > activateStart);

        string activateBody = source[activateStart..deactivateStart];
        Assert.Contains("ResolveTunnelsForRunningInstances", activateBody);
    }

}
