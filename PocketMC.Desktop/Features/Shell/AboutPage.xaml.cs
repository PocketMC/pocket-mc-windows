using PocketMC.Infrastructure.Configuration;
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
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

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

            TxtAboutTitle.Text = $"About {AppConfig.AppName}";
            TxtVersion.Text = $"Version {AppConfig.AppVersion}";
            TxtAppTitle.Text = $"{AppConfig.AppName} Desktop";
            TxtAppDescription.Text = AppConfig.AppDescription;
            TxtOrgName.Text = AppConfig.OrganizationName;
            TxtOrgTagline.Text = AppConfig.OrganizationTagline;
            TxtCommunityDesc.Text = $"Join our Discord server to get help, share tips, and connect with other {AppConfig.AppName} users.";
            TxtDonationDesc.Text = $"Support the development of {AppConfig.AppName}! If you love this project and find it useful, consider buying us a coffee or submitting feedback.";

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.EnableMouseWheelScrolling(this, AboutScrollViewer);
            _ = RefreshContributorAvatarsAsync();
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

        private void OpenOrganizationWebsite_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkOrganization);
        }

        private void OpenWebsite_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkWebsite);
        }

        private void OpenDivyGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkContributorDivy);
        }

        private void OpenSahajGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkContributorSahaj);
        }

        private void OpenJohndinglesinGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkHelperJohndinglesin);
        }

        private void OpenNexViceDiscord_Click(object sender, RoutedEventArgs e)
        {
            OpenLink(AppConfig.LinkDonatorNexVice);
        }

        private async Task RefreshContributorAvatarsAsync()
        {
            try
            {
                await Task.WhenAll(
                    TryLoadOnlineAvatarAsync(ImgDivyAvatar, "https://github.com/divyviradiya2.png?size=256"),
                    TryLoadOnlineAvatarAsync(ImgSahajAvatar, "https://github.com/SizWinz.png?size=256"),
                    TryLoadOnlineAvatarAsync(ImgJohndinglesinAvatar, "https://github.com/Johndinglesin.png?size=256")
                );
            }
            catch
            {
            }
        }

        private async Task TryLoadOnlineAvatarAsync(Image? targetImage, string avatarUrl)
        {
            if (targetImage == null) return;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PocketMC-Desktop");
                var bytes = await client.GetByteArrayAsync(avatarUrl).ConfigureAwait(false);
                if (bytes != null && bytes.Length > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var bitmap = new BitmapImage();
                        using var ms = new MemoryStream(bytes);
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        targetImage.Source = bitmap;
                    });
                }
            }
            catch
            {
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


