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

    [Fact]
    public void BuildRequests_FabricVoiceChatConfig_AddsSimpleVoiceChatUdpRequest()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Voice Fabric", serverType: "Fabric");

        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("config", "voicechat", "voicechat-server.properties"),
            """
            # Simple Voice Chat
            port=24454
            bind_address=*
            """);

        IReadOnlyList<PortCheckRequest> requests = service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id));

        Assert.Equal(2, requests.Count);
        PortCheckRequest voiceRequest = Assert.Single(requests.Where(x => x.BindingRole == PortBindingRole.SimpleVoiceChat));
        Assert.Equal("Simple Voice Chat", voiceRequest.DisplayName);
        Assert.Equal(24454, voiceRequest.Port);
        Assert.Equal(PortProtocol.Udp, voiceRequest.Protocol);
        Assert.Equal(PortEngine.SimpleVoiceChat, voiceRequest.Engine);
        Assert.Null(voiceRequest.BindAddress);
    }

    [Fact]
    public void BuildRequests_PluginVoiceChatConfig_AddsSimpleVoiceChatUdpRequest()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Voice Plugin", serverType: "Paper");

        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("plugins", "voicechat", "voicechat-server.properties"),
            "port=24455");

        PortCheckRequest voiceRequest = Assert.Single(
            service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id))
                .Where(x => x.BindingRole == PortBindingRole.SimpleVoiceChat));

        Assert.Equal(24455, voiceRequest.Port);
        Assert.Equal(PortProtocol.Udp, voiceRequest.Protocol);
    }

    [Fact]
    public void BuildRequests_VoiceChatJarWithoutConfig_AddsDefaultPendingRequest()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Voice Pending", serverType: "Fabric");

        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");

        PortCheckRequest voiceRequest = Assert.Single(
            service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id))
                .Where(x => x.BindingRole == PortBindingRole.SimpleVoiceChat));

        Assert.Equal(24454, voiceRequest.Port);
        Assert.Equal(PortProtocol.Udp, voiceRequest.Protocol);
        Assert.Equal("Simple Voice Chat", voiceRequest.DisplayName);
    }

    [Fact]
    public void BuildRequests_VoiceChatConfigWithoutPort_DefaultsTo24454()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Voice Default", serverType: "Fabric");

        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("config", "voicechat", "voicechat-server.properties"),
            """
            # port intentionally omitted
            bind_address=0.0.0.0
            """);

        PortCheckRequest voiceRequest = Assert.Single(
            service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id))
                .Where(x => x.BindingRole == PortBindingRole.SimpleVoiceChat));

        Assert.Equal(24454, voiceRequest.Port);
    }

    [Fact]
    public void BuildRequests_VoiceChatConfigWithCommentsAndWhitespace_ParsesCustomPort()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = workspace.CreatePortPreflightService();
        var metadata = workspace.CreateInstance("Voice Custom", serverType: "Fabric");

        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("config", "voicechat", "voicechat-server.properties"),
            """
               # leading comment
               port = 25001
               bind_address = 127.0.0.1
               voice_host = old.example.com:24454
            """);

        PortCheckRequest voiceRequest = Assert.Single(
            service.BuildRequests(metadata, workspace.GetInstancePath(metadata.Id))
                .Where(x => x.BindingRole == PortBindingRole.SimpleVoiceChat));

        Assert.Equal(25001, voiceRequest.Port);
        Assert.Equal("127.0.0.1", voiceRequest.BindAddress);
    }

    [Fact]
    public void PatchVoiceHost_PreservesExistingVoiceChatPropertiesContent()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Patch", serverType: "Fabric");
        string relativePath = Path.Combine("config", "voicechat", "voicechat-server.properties");
        string original = string.Join(
            "\r\n",
            "# Simple Voice Chat settings",
            "port = 25001",
            "bind_address=*",
            "voice_host = old.example.com:24454",
            "voice_host = duplicate.example.com:24454",
            "keep_me = yes",
            string.Empty);
        workspace.WriteFile(
            metadata.Id,
            relativePath,
            original);
        string configPath = Path.Combine(workspace.GetInstancePath(metadata.Id), relativePath);

        Assert.True(SimpleVoiceChatConfigService.TryPatchVoiceHost(configPath, "voice.playit.gg:30000"));

        string updated = File.ReadAllText(configPath);
        Assert.Contains("# Simple Voice Chat settings", updated);
        Assert.Contains("port = 25001", updated);
        Assert.Contains("bind_address=*", updated);
        Assert.Contains("voice_host = voice.playit.gg:30000", updated);
        Assert.Contains("keep_me = yes", updated);
        Assert.DoesNotContain("old.example.com:24454", updated);
        Assert.DoesNotContain("duplicate.example.com:24454", updated);
        Assert.Equal(1, CountOccurrences(updated, "voice_host"));
        Assert.Contains("\r\n", updated);
    }

    [Fact]
    public void SimpleVoiceChatConfigService_UsesAtomicWrites_ForConfigMutations()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Networking",
            "SimpleVoiceChatProperties.cs"));

        Assert.DoesNotContain("File.WriteAllText(configPath", source);
        Assert.Contains("FileUtils.AtomicWriteAllText(configPath", source);
    }

    private static int CountOccurrences(string value, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
