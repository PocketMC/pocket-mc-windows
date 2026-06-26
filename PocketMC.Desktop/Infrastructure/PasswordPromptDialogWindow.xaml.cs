using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Infrastructure
{
    public partial class PasswordPromptDialogWindow : FluentWindow
    {
        public string? Password { get; private set; }

        public PasswordPromptDialogWindow(string title, string message)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            // Focus password box when dialog opens
            Loaded += (s, e) => PwdInput.Focus();
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            Password = PwdInput.Password;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PwdInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnPrimary_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancel_Click(sender, e);
            }
        }
    }
}
