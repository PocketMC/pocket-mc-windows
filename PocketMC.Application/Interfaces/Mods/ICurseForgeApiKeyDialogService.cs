using System.Threading.Tasks;

namespace PocketMC.Application.Interfaces.Mods
{
    public interface ICurseForgeApiKeyDialogService
    {
        Task<string?> PromptForApiKeyAsync();
    }
}
