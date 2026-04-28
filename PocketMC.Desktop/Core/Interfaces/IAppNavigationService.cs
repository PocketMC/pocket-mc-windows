using System;
using System.Windows.Controls;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Core.Interfaces
{
    public enum DetailRouteKind
    {
        NewInstance,
        ServerSettings,
        PluginBrowser,
        ServerConsole,
        PlayerManagement,
        PlayitSetupWizard,
        ImageCrop
    }

    public enum DetailBackNavigation
    {
        Dashboard,
        Tunnel,
        PreviousDetail
    }

    public interface IAppNavigationService
    {
        void Initialize(IShellHost shellHost);
        bool NavigateToDashboard();
        bool NavigateToTunnel();
        bool NavigateToShellPage(Type pageType);
        bool NavigateToDetailPage(
            Page page,
            string breadcrumbLabel,
            DetailRouteKind routeKind,
            DetailBackNavigation backNavigation,
            bool clearDetailStack = false);
        bool NavigateBack();
    }
}
