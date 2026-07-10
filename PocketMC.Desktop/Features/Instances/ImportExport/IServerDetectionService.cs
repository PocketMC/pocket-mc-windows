using System.Threading.Tasks;

namespace PocketMC.Desktop.Features.Instances.ImportExport
{
    public interface IServerDetectionService
    {
        Task<(string ServerType, string MinecraftVersion)> DetectServerTypeAndVersionAsync(string folderPath);
    }
}
