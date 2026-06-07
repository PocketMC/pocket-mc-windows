namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteDashboardWebSourceTests
{
    [Fact]
    public void Stylesheet_EnsuresHiddenViewsCannotBeOverriddenByPanelDisplayRules()
    {
        string css = ReadWebFile("styles.css");

        Assert.Contains("[hidden]", css, StringComparison.Ordinal);
        Assert.Contains("display: none !important", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Stylesheet_UsesMinimalRemotePalette()
    {
        string css = ReadWebFile("styles.css");

        Assert.Contains("--black:", css, StringComparison.Ordinal);
        Assert.Contains("--green:", css, StringComparison.Ordinal);
        Assert.Contains("--red:", css, StringComparison.Ordinal);
        Assert.Contains("--white:", css, StringComparison.Ordinal);
        Assert.Contains("--grey:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--blue", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--amber", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_UsesIconLedPrimaryControls()
    {
        string html = ReadWebFile("index.html");
        string script = ReadWebFile("app.js");

        Assert.Contains("class=\"button-icon\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"startButton\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"stopButton\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"restartButton\"", html, StringComparison.Ordinal);
        Assert.Contains("setButtonLabel", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_DoesNotAutomaticallyConsumePairingTokenOnPageLoad()
    {
        string script = ReadWebFile("app.js");

        Assert.Contains("showPairPrompt", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if (pairingTokenFromUrl()) {\n  pairDevice();", script, StringComparison.Ordinal);
        Assert.Contains("if (deviceToken) {", script, StringComparison.Ordinal);
        Assert.Contains("await openDashboard();", script, StringComparison.Ordinal);
        Assert.Contains("if (pairingTokenFromUrl()) {\n      history.replaceState({}, \"\", \"/remote/index.html\");", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_ReconnectsConsoleWhenServerStartsAfterInitialLoad()
    {
        string script = ReadWebFile("app.js");

        Assert.Contains("ensureConsoleConnection", script, StringComparison.Ordinal);
        Assert.Contains("await refreshEverything({ reconnectConsole: true })", script, StringComparison.Ordinal);
    }

    private static string ReadWebFile(string fileName) =>
        File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "RemoteControl",
            "Web",
            fileName)).Replace("\r", "");
}
