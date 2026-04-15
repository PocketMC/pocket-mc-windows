using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Features.Shell
{
    public partial class AboutPage : Page
    {
        private readonly IDialogService _dialogService;

        public AboutPage(IDialogService dialogService)
        {
            InitializeComponent();
            _dialogService = dialogService;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            TxtVersion.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
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

        private void CopyDiscordInvite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("https://discord.gg/h27uNCaxPH");
                _dialogService.ShowMessage("Copied", "Discord invite copied to clipboard.");
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Unable to copy invite", ex.Message);
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