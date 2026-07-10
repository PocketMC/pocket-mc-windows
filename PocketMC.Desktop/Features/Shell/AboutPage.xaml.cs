using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.WhatsNew;
using PocketMC.Desktop.Features.Settings;
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

            TxtVersion.Text = $"Version {AppConfig.AppVersion}";

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
            var invite = AppConfig.LinkDiscord;
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
            bool ok = await Infrastructure.ClipboardHelper.TrySetTextAsync(AppConfig.LinkDiscord);
            if (ok)
                _dialogService.ShowMessage("Copied", "Discord invite copied to clipboard.");
            else
                _dialogService.ShowMessage("Clipboard Error", "Failed to copy. The clipboard may be locked by another application.");
        }

        private void OpenFeedbackForm_Click(object sender, RoutedEventArgs e)
        {
            var formUrl = AppConfig.LinkFeedback;
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
            var url = AppConfig.LinkYouTube;
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
            var url = AppConfig.LinkReddit;
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

        private void OpenInstagram_Click(object sender, RoutedEventArgs e)
        {
            var url = AppConfig.LinkInstagram;
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
            var repoUrl = AppConfig.LinkGitHub;
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
            var donationUrl = AppConfig.LinkDonation;
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


