using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Moq;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Navigation;
using Xunit;

namespace PocketMC.Desktop.Tests.Navigation;

public class AppNavigationServiceResumptionTests
{
    [Fact]
    public void ReturningToDashboard_FromAppSettings_ResumesActiveDetailPage()
    {
        RunInSta(() =>
        {
            var mockUiState = new Mock<IShellUIStateService>();
            var mockImport = new Mock<IInstanceImportService>();
            var mockExport = new Mock<IInstanceExportService>();
            var mockShellHost = new Mock<IShellHost>();

            mockShellHost.Setup(h => h.ShowShellPage(It.IsAny<Type>(), It.IsAny<object>())).Returns(true);
            mockShellHost.Setup(h => h.ShowDetailPage(It.IsAny<Page>(), It.IsAny<string>())).Returns(true);

            var navService = new AppNavigationService(mockUiState.Object, mockImport.Object, mockExport.Object);
            navService.Initialize(mockShellHost.Object);

            // 1. Initial navigation to Dashboard
            navService.NavigateToShellPage(typeof(DashboardPage));

            // 2. Open a detail page (e.g. ServerConsole)
            var detailPage = new Page();
            navService.NavigateToDetailPage(
                detailPage,
                "Console",
                DetailRouteKind.ServerConsole,
                DetailBackNavigation.Dashboard);

            // 3. User clicks Settings
            navService.NavigateToShellPage(typeof(AppSettingsPage));
            mockShellHost.Verify(h => h.ShowShellPage(typeof(AppSettingsPage), null), Times.Once);

            // 4. User clicks Home (Dashboard) -> should resume detail page
            bool resumed = navService.NavigateToShellPage(typeof(DashboardPage));
            Assert.True(resumed);
            mockShellHost.Verify(h => h.ShowDetailPage(detailPage, "Console"), Times.Exactly(2)); // Once when opened, once when resumed

            // 5. User clicks Home again while on detail page -> should navigate to root Dashboard
            bool rootNavigated = navService.NavigateToShellPage(typeof(DashboardPage));
            Assert.True(rootNavigated);
            mockShellHost.Verify(h => h.ShowShellPage(typeof(DashboardPage), null), Times.Exactly(2)); // Initial + root reset
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw new Exception("STA test thread threw exception", exception);
        }
    }

    [Fact]
    public void ReturningToDashboard_WithoutDetailPages_NavigatesToDashboardRoot()
    {
        var mockUiState = new Mock<IShellUIStateService>();
        var mockImport = new Mock<IInstanceImportService>();
        var mockExport = new Mock<IInstanceExportService>();
        var mockShellHost = new Mock<IShellHost>();

        mockShellHost.Setup(h => h.ShowShellPage(It.IsAny<Type>(), It.IsAny<object>())).Returns(true);

        var navService = new AppNavigationService(mockUiState.Object, mockImport.Object, mockExport.Object);
        navService.Initialize(mockShellHost.Object);

        // 1. Navigate to Settings
        navService.NavigateToShellPage(typeof(AppSettingsPage));

        // 2. Navigate to Dashboard (no detail page was ever opened)
        bool navigated = navService.NavigateToShellPage(typeof(DashboardPage));
        Assert.True(navigated);
        mockShellHost.Verify(h => h.ShowShellPage(typeof(DashboardPage), null), Times.Once);
    }
}
