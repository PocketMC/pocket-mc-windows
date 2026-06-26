using System;
using System.Diagnostics;
using System.Windows;

namespace PocketMC.Desktop.Features.WhatsNew
{
    /// <summary>
    /// Code-behind for the What's New dialog window.
    /// Displays changelog sections or a fallback message after an app update.
    /// </summary>
    public partial class WhatsNewWindow : Wpf.Ui.Controls.FluentWindow
    {
        private const string GitHubReleasesUrl = "https://github.com/PocketMC/pocket-mc-windows/releases";

        private readonly string _version;

        public WhatsNewWindow(ChangelogEntry? changelog, string version)
        {
            _version = version;

            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            // Re-assert accent color to prevent FluentWindow from reverting to system accent.
            try
            {
                if (System.Windows.Application.Current is App app)
                {
                    var accentService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                        .GetService<PocketMC.Desktop.Features.Shell.AccentColorService>(app.Services);
                    accentService?.ReassertAccent();
                }
            }
            catch
            {
                // Non-critical â€” dialog will still work.
            }

            if (changelog != null && changelog.Sections.Count > 0)
            {
                ConfigureWithChangelog(changelog);
            }
            else
            {
                ConfigureWithFallback();
            }
        }

        private void ConfigureWithChangelog(ChangelogEntry changelog)
        {
            TxtHeader.Text = $"What's New in v{_version}";
            TxtSubheader.Text = "Here's what changed in this update.";

            SectionsPanel.ItemsSource = changelog.Sections;
            SectionsPanel.Visibility = Visibility.Visible;
            FallbackPanel.Visibility = Visibility.Collapsed;
        }

        private void ConfigureWithFallback()
        {
            TxtHeader.Text = $"ðŸŽ‰ Updated to v{_version}";
            TxtSubheader.Text = string.Empty;

            TxtFallback.Text = "Pocket MC has been updated successfully.\nThank you for updating!";
            SectionsPanel.Visibility = Visibility.Collapsed;
            FallbackPanel.Visibility = Visibility.Visible;
            BtnFullChangelog.Visibility = Visibility.Collapsed;
        }

        private void GotIt_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ViewFullChangelog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = $"{GitHubReleasesUrl}/tag/v{_version}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // If the browser cannot be opened, silently ignore.
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}


