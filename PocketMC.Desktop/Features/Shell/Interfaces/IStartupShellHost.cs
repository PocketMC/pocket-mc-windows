namespace PocketMC.Desktop.Features.Shell.Interfaces
{
    public interface IStartupShellHost
    {
        void ShowRootDirectorySetup();
        void CompleteRootDirectorySetup();
        void ApplyTheme();
        void RequestMicaUpdate();
        bool NavigateToDashboard();
        bool NavigateToTunnel();

        void ShowError(string title, string message);
        void ShutdownApplication();
        void ShowWhatsNewDialog(PocketMC.Infrastructure.WhatsNew.ChangelogEntry? changelog, string version);
    }
}

