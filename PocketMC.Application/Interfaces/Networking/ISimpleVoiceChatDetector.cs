using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

public interface ISimpleVoiceChatDetector
{
    SimpleVoiceChatDetection Detect(string? serverDir);
}
