using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Tests;

public sealed class PortPreflightServiceTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("65536")]
    public void Check_InvalidConfiguredPorts_ReturnsInvalidRange(string rawPort)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Invalid Port", serverType: "Paper");

        workspace.WriteServerProperties(metadata.Id, $"server-port={rawPort}");

        PortCheckResult result = service.Check(metadata, workspace.GetInstancePath(metadata.Id));

        Assert.False(result.IsSuccessful);
        Assert.Equal(PortFailureCode.InvalidRange, result.FailureCode);
        Assert.Equal(25565, result.Recommendations.Single().SuggestedPort);
    }

    [Fact]
    public void BuildRequests_MalformedConfiguredPort_FallsBackToDefaultJavaPort()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Malformed Port", serverType: "Paper");

        workspace.WriteServerProperties(metadata.Id, "server-port=not-a-number");

        PortCheckRequest request = Assert.Single(service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id)));
        PortCheckResult result = service.Check(metadata, workspace.GetInstancePath(metadata.Id));

        Assert.Equal(25565, request.Port);
        Assert.Equal(PortProtocol.Tcp, request.Protocol);
        Assert.Equal(PortBindingRole.JavaServer, request.BindingRole);
        Assert.Equal(PortEngine.Java, request.Engine);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Check_WhenAnotherPocketMcInstanceUsesSamePortButIsNotRunning_ReturnsSuccess()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var first = workspace.CreateInstance("Alpha", serverType: "Paper");
        var second = workspace.CreateInstance("Beta", serverType: "Paper");

        workspace.WriteServerProperties(first.Id, "server-port=25570");
        workspace.WriteServerProperties(second.Id, "server-port=25570");

        PortCheckResult result = service.Check(second, workspace.GetInstancePath(second.Id));

        Assert.True(result.IsSuccessful);
        Assert.Equal(PortFailureCode.None, result.FailureCode);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void BuildRequests_JavaServerWithGeyserConfig_ReturnsProtocolAwareRequests()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Crossplay", serverType: "Paper");
        metadata.HasGeyser = true;
        metadata.GeyserBedrockPort = 19140;
        workspace.SaveMetadata(metadata);

        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("plugins", "Geyser-Spigot", "config.yml"),
            """
            bedrock:
              address: 0.0.0.0
              port: 19140
              clone-remote-port: false
            """);

        IReadOnlyList<PortCheckRequest> requests = service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id));

        Assert.Equal(2, requests.Count);

        PortCheckRequest javaRequest = Assert.Single(requests.Where(x => x.BindingRole == PortBindingRole.JavaServer));
        Assert.Equal(PortProtocol.Tcp, javaRequest.Protocol);
        Assert.Equal(PortEngine.Java, javaRequest.Engine);
        Assert.Equal(25565, javaRequest.Port);

        PortCheckRequest geyserRequest = Assert.Single(requests.Where(x => x.BindingRole == PortBindingRole.GeyserBedrock));
        Assert.Equal(PortProtocol.Udp, geyserRequest.Protocol);
        Assert.Equal(PortEngine.Geyser, geyserRequest.Engine);
        Assert.Equal(19140, geyserRequest.Port);
        Assert.Equal("Geyser Bedrock", geyserRequest.DisplayName);
    }

    [Theory]
    [InlineData("Bedrock", PortEngine.BedrockDedicated, PortBindingRole.BedrockServer)]
    [InlineData("Pocketmine-MP", PortEngine.PocketMine, PortBindingRole.PocketMineServer)]
    public void BuildRequests_NativeBedrockEngines_UseUdpAndEngineSpecificMetadata(
        string serverType,
        PortEngine expectedEngine,
        PortBindingRole expectedRole)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Native", serverType: serverType);

        workspace.WriteServerProperties(metadata.Id, "server-port=19155");

        PortCheckRequest request = Assert.Single(service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id)));

        Assert.Equal(19155, request.Port);
        Assert.Equal(PortProtocol.Udp, request.Protocol);
        Assert.Equal(expectedEngine, request.Engine);
        Assert.Equal(expectedRole, request.BindingRole);
    }
}
