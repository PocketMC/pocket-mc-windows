using System.Threading.Tasks;

namespace PocketMC.Desktop.Core.Interfaces
{
    public interface IApplicationLifecycleService
    {
        /// <summary>
        /// Gracefully shuts down all active internal systems (servers, tunnels, etc.)
        /// required before an application restart or exit.
        /// </summary>
        Task GracefulShutdownAsync();
    }
}
