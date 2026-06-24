namespace PocketMC.Desktop.Features.Networking;

public interface ISimpleVoiceChatDetector
{
    SimpleVoiceChatDetection Detect(string? serverDir);
}
