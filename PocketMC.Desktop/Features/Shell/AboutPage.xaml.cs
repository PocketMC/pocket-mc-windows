using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.WhatsNew;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Shell
{
    public partial class AboutPage : Page
    {
        private readonly IDialogService _dialogService;
        private readonly WhatsNewService _whatsNewService;

        public AboutPage(IDialogService dialogService, WhatsNewService whatsNewService)
        {
            InitializeComponent();
            _dialogService = dialogService;
            _whatsNewService = whatsNewService;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            TxtVersion.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.EnableMouseWheelScrolling(this, AboutScrollViewer);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.DisableMouseWheelScrolling(this);
        }

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            var invite = "https://discord.gg/h27uNCaxPH";
            try
            {
                var psi = new ProcessStartInfo(invite) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to open link", ex.Message);
            }
        }

        private async void CopyDiscordInvite_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await Infrastructure.ClipboardHelper.TrySetTextAsync("https://discord.gg/h27uNCaxPH");
            if (ok)
                _dialogService.ShowMessage("Copied", "Discord invite copied to clipboard.");
            else
                _dialogService.ShowMessage("Clipboard Error", "Failed to copy. The clipboard may be locked by another application.");
        }

        private void OpenFeedbackForm_Click(object sender, RoutedEventArgs e)
        {
            var formUrl = "https://docs.google.com/forms/d/e/1FAIpQLSd6cNMawAbvoELxqIF_FobaC3DptKnjQxViDh9XLcyJdNbTAQ/viewform?usp=dialog";
            try
            {
                var psi = new ProcessStartInfo(formUrl) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to open link", ex.Message);
            }
        }

        private void OpenYouTube_Click(object sender, RoutedEventArgs e)
        {
            var url = "https://www.youtube.com/@OfficialPocketMC";
            try
            {
                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to open link", ex.Message);
            }
        }

        private void OpenReddit_Click(object sender, RoutedEventArgs e)
        {
            var url = "https://www.reddit.com/r/PocketMC/";
            try
            {
                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to open link", ex.Message);
            }
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            var repoUrl = "https://github.com/PocketMC/pocket-mc-windows";
            try
            {
                var psi = new ProcessStartInfo(repoUrl) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to open link", ex.Message);
            }
        }

        private void OpenDonationPage_Click(object sender, RoutedEventArgs e)
        {
            var donationUrl = "https://buymeacoffee.com/sahaj33";
            try
            {
                var psi = new ProcessStartInfo(donationUrl) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to open link", ex.Message);
            }
        }

        private void WhatsNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string version = _whatsNewService.GetCurrentVersion();
                ChangelogEntry? changelog = _whatsNewService.LoadChangelog();

                var window = new WhatsNewWindow(changelog, version);
                try
                {
                    var mainWindow = System.Windows.Application.Current?.MainWindow;
                    if (mainWindow != null && mainWindow.IsLoaded && mainWindow.IsVisible)
                    {
                        window.Owner = mainWindow;
                    }
                }
                catch { }

                // Manual access does NOT call MarkAsSeen
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", $"Could not load changelog: {ex.Message}");
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}


