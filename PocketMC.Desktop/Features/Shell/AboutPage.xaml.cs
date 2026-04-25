using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Shell
{
    public partial class AboutPage : Page
    {
        private readonly IDialogService _dialogService;
        private readonly LocalizationService _localizationService;

        public AboutPage(IDialogService dialogService, LocalizationService localizationService)
        {
            InitializeComponent();
            _dialogService = dialogService;
            _localizationService = localizationService;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            TxtVersion.Text = string.Format(_localizationService.GetString("AboutPageVersionLabel"),
                $"{version?.Major}.{version?.Minor}.{version?.Build}");
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
                _dialogService.ShowMessage(_localizationService.GetString("UnableToOpenLinkTitle"), ex.Message);
            }
        }

        private void CopyDiscordInvite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("https://discord.gg/h27uNCaxPH");
                _dialogService.ShowMessage(_localizationService.GetString("CopiedTitle"), _localizationService.GetString("CopiedToClipboardMessage"));
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(_localizationService.GetString("UnableToCopyInviteTitle"), ex.Message);
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