using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models.Tunnel;

namespace PocketMC.Application.Interfaces.Tunnels
{
    public interface IPlayitStatusService
    {
        Task<List<PlayitStatusMonitor>> GetNetworkStatusAsync(CancellationToken cancellationToken = default);
    }
}
