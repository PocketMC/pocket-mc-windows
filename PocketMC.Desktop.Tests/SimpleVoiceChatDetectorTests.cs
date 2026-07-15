using PocketMC.Desktop.Features.Networking;
using PocketMC.Infrastructure.Marketplace;

namespace PocketMC.Desktop.Tests;

public sealed class SimpleVoiceChatDetectorTests
{
    [Theory]
    [InlineData("voicechat-2.5.0.jar")]
    [InlineData("simplevoicechat-fabric-1.20.4.jar")]
    [InlineData("simple-voice-chat-fabric-1.20.4.jar")]
    public void Detect_FabricVoiceChatJar_ReturnsDetected(string jarName)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Jar", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", jarName), "jar");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.ModJar, detection.Source);
        Assert.True(detection.IsConfigPending);
        Assert.Equal(24454, detection.Port);
    }

    [Fact]
    public void Detect_GenericVoiceChatAddonWithoutConfigOrLog_DoesNotFalsePositive()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Addon", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "my-server-voicechat-addon.jar"), "jar");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.False(detection.IsDetected);
    }

    [Fact]
    public void Detect_PluginVoiceChatJar_ReturnsDetected()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Plugin Jar", serverType: "Paper");
        workspace.WriteFile(metadata.Id, Path.Combine("plugins", "voicechat-bukkit.jar"), "jar");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.PluginJar, detection.Source);
    }

    [Fact]
    public void Detect_ConfigOnly_ReturnsDetectedWithConfigPath()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Config", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        string relativePath = Path.Combine("config", "simplevoicechat", "voicechat-server.properties");
        workspace.WriteFile(metadata.Id, relativePath, "port=25000");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.ModJar, detection.Source);
        Assert.Equal(25000, detection.Port);
        Assert.EndsWith(relativePath, detection.ConfigPath);
    }

    [Fact]
    public void Detect_LogFallback_ReturnsDetected()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Log", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("logs", "latest.log"),
            "[12:00:00] [Server thread/INFO]: [voicechat] Voice chat server started at port 24460");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.Log, detection.Source);
        Assert.Equal(24460, detection.Port);
    }

    [Fact]
    public void Detect_LockedLatestLog_DoesNotThrow()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Locked Voice Log", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        string logPath = Path.Combine(workspace.GetInstancePath(metadata.Id), "logs", "latest.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, "[voicechat] Voice chat server started at port 24460");

        using var lockedLog = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        // Since the log is locked, it fails to read log, falling back to Pending(ModJar) which is IsDetected == true
        Assert.True(detection.IsDetected);
        Assert.Equal(SimpleVoiceChatDetectionSource.ModJar, detection.Source);
    }

    [Fact]
    public void Detect_ConfigOnlyWithoutJar_ReturnsNotDetected()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Leftover Config", serverType: "Fabric");
        string relativePath = Path.Combine("config", "simplevoicechat", "voicechat-server.properties");
        workspace.WriteFile(metadata.Id, relativePath, "port=25000");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.False(detection.IsDetected);
    }

    [Theory]
    [InlineData("audioplayer.jar")]
    [InlineData("sound-physics-remastered.jar")]
    public void Detect_UnrelatedAudioMod_DoesNotFalsePositive(string jarName)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Audio Mod", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", jarName), "jar");

        var detector = new SimpleVoiceChatDetector(new AddonManifestService());
        SimpleVoiceChatDetection detection = detector.Detect(workspace.GetInstancePath(metadata.Id));

        Assert.False(detection.IsDetected);
    }
}
