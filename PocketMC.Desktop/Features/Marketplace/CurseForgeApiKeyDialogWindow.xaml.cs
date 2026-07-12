using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class CurseForgeApiKeyDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        public string? ApiKey { get; private set; }

        public CurseForgeApiKeyDialogWindow()
        {
            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            // FluentWindow initialization can reset accent resources.
            // Re-assert the current accent to prevent the dialog from
            // reverting the entire application to the system accent.
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
                // Non-critical — dialog will still work with whatever accent is current.
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtApiKey.Text))
            {
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            ApiKey = TxtApiKey.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
