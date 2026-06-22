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
        bool ShowPlayitSetupDialog();
        void ShowError(string title, string message);
        void ShutdownApplication();
        void ShowWhatsNewDialog(PocketMC.Desktop.Features.WhatsNew.ChangelogEntry? changelog, string version);
    }
}

