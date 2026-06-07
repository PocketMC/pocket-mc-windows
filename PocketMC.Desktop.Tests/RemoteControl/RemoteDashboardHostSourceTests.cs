namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteDashboardHostSourceTests
{
    [Fact]
    public void PairRoute_PreservesPairingTokenQueryStringWhenRedirectingToDashboard()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "RemoteControl",
            "Hosting",
            "RemoteDashboardHost.cs"));

        Assert.Contains("MapGet(\"/pair\", (HttpContext context)", source, StringComparison.Ordinal);
        Assert.Contains("context.Request.QueryString", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet(\"/pair\", () => Results.Redirect(\"/remote/index.html\"))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TunnelBackedRemoteModes_BindDashboardToLoopbackOnly()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "RemoteControl",
            "Hosting",
            "RemoteDashboardHost.cs"));

        Assert.Contains("UsesLoopbackOnlyForRemoteTunnel(settings.AccessMode)", source, StringComparison.Ordinal);
        Assert.Contains("RemoteAccessMode.CloudflaredQuickTunnel", source, StringComparison.Ordinal);
    }
}
