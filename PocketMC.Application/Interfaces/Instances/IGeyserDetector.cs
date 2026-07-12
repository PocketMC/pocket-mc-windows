using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.Instances;

public interface IGeyserDetector
{
    bool IsGeyserInstalled(string? instancePath);
}
