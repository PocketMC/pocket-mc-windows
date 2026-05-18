using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Tests;

public sealed class SimpleVoiceChatDetectorTests
{
    [Theory]
    [InlineData("voicechat-2.5.0.jar")]
    [InlineData("simplevoicechat-fabric-1.20.4.jar")]
    [InlineData("my-server-voicechat-addon.jar")]
    public void Detect_FabricVoiceChatJar_ReturnsDetected(string jarName)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Jar", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", jarName), "jar");

        SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.ModJar, detection.Source);
        Assert.True(detection.IsConfigPending);
        Assert.Equal(24454, detection.Port);
    }

    [Fact]
    public void Detect_PluginVoiceChatJar_ReturnsDetected()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Plugin Jar", serverType: "Paper");
        workspace.WriteFile(metadata.Id, Path.Combine("plugins", "voicechat-bukkit.jar"), "jar");

        SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.PluginJar, detection.Source);
    }

    [Fact]
    public void Detect_ConfigOnly_ReturnsDetectedWithConfigPath()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Config", serverType: "Fabric");
        string relativePath = Path.Combine("config", "simplevoicechat", "voicechat-server.properties");
        workspace.WriteFile(metadata.Id, relativePath, "port=25000");

        SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.ConfigFile, detection.Source);
        Assert.Equal(25000, detection.Port);
        Assert.EndsWith(relativePath, detection.ConfigPath);
    }

    [Fact]
    public void Detect_LogFallback_ReturnsDetected()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Log", serverType: "Fabric");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("logs", "latest.log"),
            "[12:00:00] [Server thread/INFO]: [voicechat] Voice chat server started at port 24460");

        SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.Log, detection.Source);
        Assert.Equal(24460, detection.Port);
    }

    [Theory]
    [InlineData("audioplayer.jar")]
    [InlineData("sound-physics-remastered.jar")]
    public void Detect_UnrelatedAudioMod_DoesNotFalsePositive(string jarName)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Audio Mod", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", jarName), "jar");

        SimpleVoiceChatDetection detection = SimpleVoiceChatDetector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.False(detection.IsDetected);
    }
}
