using System;
using System.IO;
using PocketMC.Desktop.Tests.TestSupport.Utilities;
using Xunit;

namespace PocketMC.Desktop.Tests.Accessibility;

public class KeyboardNavigationAndAccessibilityTests
{
    [Theory]
    [InlineData("PocketMC.Desktop/Features/Instances/Dialogs/ServerCrashDialogWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Tunnel/PlayitSetupWizardDialog.xaml")]
    [InlineData("PocketMC.Desktop/Features/Networking/PortConflictWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Marketplace/DependencyConfirmationWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Marketplace/ModpackInstallDialogWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Marketplace/AddonInstallDialogWindow.xaml")]
    public void ModalDialogs_HaveTabCycleEnabled(string relativeXamlPath)
    {
        string[] parts = relativeXamlPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string fullPath = TestSourceFileResolver.Resolve(parts);
        Assert.True(File.Exists(fullPath), $"File not found: {fullPath}");

        string content = File.ReadAllText(fullPath);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", content);
    }

    [Theory]
    [InlineData("PocketMC.Desktop/Features/Instances/Dialogs/ServerCrashDialogWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Tunnel/PlayitSetupWizardDialog.xaml")]
    [InlineData("PocketMC.Desktop/Features/Networking/PortConflictWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Marketplace/DependencyConfirmationWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Marketplace/ModpackInstallDialogWindow.xaml")]
    [InlineData("PocketMC.Desktop/Features/Marketplace/AddonInstallDialogWindow.xaml")]
    public void ModalDialogs_HaveCancelButtonDefined(string relativeXamlPath)
    {
        string[] parts = relativeXamlPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string fullPath = TestSourceFileResolver.Resolve(parts);
        Assert.True(File.Exists(fullPath), $"File not found: {fullPath}");

        string content = File.ReadAllText(fullPath);
        Assert.Contains("IsCancel=\"True\"", content);
    }

    [Fact]
    public void MainWindow_NavigationViewItems_HaveAccessibleNamesAndHelpTexts()
    {
        string xamlPath = TestSourceFileResolver.Resolve("PocketMC.Desktop", "Features", "Shell", "MainWindow.xaml");
        string content = File.ReadAllText(xamlPath);

        Assert.Contains("AutomationProperties.Name=\"Dashboard (Ctrl+1)\"", content);
        Assert.Contains("AutomationProperties.Name=\"Tunnel (Ctrl+2)\"", content);
        Assert.Contains("AutomationProperties.Name=\"Remote Control (Ctrl+3)\"", content);
        Assert.Contains("AutomationProperties.Name=\"Runtimes (Ctrl+4)\"", content);
        Assert.Contains("AutomationProperties.Name=\"Settings (Ctrl+5 or Ctrl+,)\"", content);
        Assert.Contains("AutomationProperties.Name=\"About (Ctrl+6)\"", content);
    }

    [Fact]
    public void DashboardPage_InstanceCards_HaveAccessibleKeyboardFocusAndNames()
    {
        string xamlPath = TestSourceFileResolver.Resolve("PocketMC.Desktop", "Features", "Dashboard", "DashboardPage.xaml");
        string content = File.ReadAllText(xamlPath);

        Assert.Contains("Focusable=\"True\"", content);
        Assert.Contains("IsTabStop=\"True\"", content);
        Assert.Contains("KeyDown=\"Card_KeyDown\"", content);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name, StringFormat='Server instance {0}'}\"", content);
    }

    [Theory]
    [InlineData(typeof(PocketMC.Desktop.Features.Console.ServerConsolePage))]
    [InlineData(typeof(PocketMC.Desktop.Features.Settings.ServerSettingsPage))]
    [InlineData(typeof(PocketMC.Desktop.Features.InstanceCreation.NewInstancePage))]
    [InlineData(typeof(PocketMC.Desktop.Features.Instances.ImportExport.InstanceImportPage))]
    [InlineData(typeof(PocketMC.Desktop.Features.Instances.ImportExport.InstanceExportPage))]
    public void DetailPages_ImplementISupportsKeyboardBackNavigation(Type pageType)
    {
        Assert.True(
            typeof(PocketMC.Desktop.Features.Shell.Interfaces.ISupportsKeyboardBackNavigation).IsAssignableFrom(pageType),
            $"{pageType.Name} must implement ISupportsKeyboardBackNavigation");
    }
}
