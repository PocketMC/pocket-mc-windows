namespace PocketMC.Desktop.Core.Interfaces
{
    public interface INotificationService
    {
        void ShowInformation(string title, string message);
        void ShowServerOnline(string serverName, string version, string loaderType);
    }
}
