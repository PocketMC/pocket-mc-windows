using System.Threading.Tasks;

namespace PocketMC.Application.Interfaces
{
    public interface ITelemetryService
    {
        void Initialize();
        void Shutdown();
        Task ReportServerActionAsync(string action);
    }
}
