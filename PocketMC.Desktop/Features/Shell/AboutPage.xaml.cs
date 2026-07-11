using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Core.Interfaces;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.WhatsNew;
using PocketMC.Infrastructure.WhatsNew;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.OS;

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

        private void OpenLink(string url)
        {
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

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkDiscord);
        }

        private async void CopyDiscordInvite_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await ClipboardHelper.TrySetTextAsync(AppConfig.LinkDiscord);
            if (ok)
                _dialogService.ShowMessage("Copied", "Discord invite copied to clipboard.");
            else
                _dialogService.ShowMessage("Clipboard Error", "Failed to copy. The clipboard may be locked by another application.");
        }

        private void OpenFeedbackForm_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkFeedback);
        }

        private void OpenYouTube_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkYouTube);
        }

        private void OpenReddit_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkReddit);
        }

        private void OpenInstagram_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkInstagram);
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkGitHub);
        }

        private void OpenDonationPage_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkDonation);
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


