using System.Threading.Tasks;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.Instances
{
    public interface IServerDetectionService
    {
        Task<(string ServerType, string MinecraftVersion)> DetectServerTypeAndVersionAsync(string folderPath);
    }
}
