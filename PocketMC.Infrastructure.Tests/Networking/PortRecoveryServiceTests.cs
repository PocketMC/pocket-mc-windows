using PocketMC.Infrastructure.Networking;
using PocketMC.Infrastructure.Tests.TestSupport.Fixtures;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Tests.Networking;

public sealed class PortRecoveryServiceTests
{
    [Fact]
    public void Recommend_ForTransientFailure_ReturnsRetryWithBackoff()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PortRecoveryService service = workspace.CreatePortRecoveryService();
        var request = new PortCheckRequest(
            19132,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            instanceId: Guid.NewGuid(),
            instanceName: "Bedrock",
            displayName: "Bedrock server");
        var result = new PortCheckResult(
            request,
            isSuccessful: false,
            canBindLocally: false,
            failureCode: PortFailureCode.PlayitAgentOffline,
            failureMessage: "Agent offline.");

        PortRecoveryRecommendation recommendation = service.Recommend(result, attemptNumber: 0, allowAutoPortSwitch: false);

        Assert.Equal(PortRecoveryAction.WaitWithBackoff, recommendation.Action);
        Assert.True(recommendation.IsTransient);
        Assert.Equal(TimeSpan.FromSeconds(1), recommendation.RetryDelay);
        Assert.Equal(request.Port, recommendation.SuggestedPort);
    }

    [Fact]
    public void Recommend_ForConflictFailure_ReturnsFallbackPortSuggestion()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PortRecoveryService service = workspace.CreatePortRecoveryService();
        int requestPort = Math.Max(25565, workspace.GetAvailableTcpPort());
        var request = new PortCheckRequest(
            requestPort,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            bindAddress: "127.0.0.1",
            instanceId: Guid.NewGuid(),
            instanceName: "Java",
            displayName: "Java server");
        var result = new PortCheckResult(
            request,
            isSuccessful: false,
            canBindLocally: false,
            failureCode: PortFailureCode.TcpConflict,
            failureMessage: "Port already in use.");

        PortRecoveryRecommendation recommendation = service.Recommend(result, attemptNumber: 3, allowAutoPortSwitch: false);

        Assert.Equal(PortRecoveryAction.SuggestNextFreePort, recommendation.Action);
        Assert.False(recommendation.IsTransient);
        Assert.True(recommendation.SuggestedPort.HasValue);
        Assert.NotEqual(requestPort, recommendation.SuggestedPort.Value);
    }

    [Fact]
    public void Recommend_ForPersistentFailure_ReturnsHardFailRecommendation()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PortRecoveryService service = workspace.CreatePortRecoveryService();
        var request = new PortCheckRequest(
            25565,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            instanceId: Guid.NewGuid(),
            instanceName: "Java",
            displayName: "Java server");
        var result = new PortCheckResult(
            request,
            isSuccessful: false,
            canBindLocally: false,
            failureCode: PortFailureCode.PlayitTokenInvalid,
            failureMessage: "Token invalid.");

        PortRecoveryRecommendation recommendation = service.Recommend(result, attemptNumber: 0, allowAutoPortSwitch: false);

        Assert.Equal(PortRecoveryAction.Abort, recommendation.Action);
        Assert.False(recommendation.IsTransient);
        Assert.True(recommendation.RequiresUserAction);
        Assert.Null(recommendation.SuggestedPort);
    }

    [Fact]
    public void FindNextFreePort_SkipsPortsConfiguredOnOtherInstances()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortRecoveryService();

        // Create an existing instance configured for 25566
        var existing = workspace.CreateInstance("Existing Server", serverType: "Paper");
        existing.ServerPort = 25566;
        workspace.SaveMetadata(existing);
        workspace.WriteServerProperties(existing.Id, "server-port=25566");

        var newServerReq = new PortCheckRequest(
            25565,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            instanceId: Guid.NewGuid(),
            instanceName: "New Server");

        int? freePort = service.FindNextFreePort(newServerReq);

        Assert.NotNull(freePort);
        Assert.NotEqual(25565, freePort.Value);
        Assert.NotEqual(25566, freePort.Value); // Must have skipped 25566 because existing instance owns it!
    }

    [Fact]
    public void FindNextFreePort_ForBedrock_SearchesNearBedrockDefault()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortRecoveryService();

        var bedrockReq = new PortCheckRequest(
            19132,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            instanceId: Guid.NewGuid(),
            instanceName: "Bedrock Server");

        int? freePort = service.FindNextFreePort(bedrockReq);

        Assert.NotNull(freePort);
        Assert.True(freePort.Value >= 19133);
    }
}

