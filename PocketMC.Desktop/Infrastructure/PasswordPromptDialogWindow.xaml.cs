using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Infrastructure
{
    public partial class PasswordPromptDialogWindow : FluentWindow
    {
        public string? Username { get; private set; }
        public string? Password { get; private set; }

        public PasswordPromptDialogWindow(string title, string message, bool askUsername, bool askPassword)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            if (!askUsername) TxtUsernameInput.Visibility = Visibility.Collapsed;
            if (!askPassword) PwdInput.Visibility = Visibility.Collapsed;

            // Focus appropriate input box when dialog opens
            Loaded += (s, e) => 
            {
                if (askUsername) TxtUsernameInput.Focus();
                else if (askPassword) PwdInput.Focus();
            };
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            Username = TxtUsernameInput.Visibility == Visibility.Visible ? TxtUsernameInput.Text : null;
            Password = PwdInput.Visibility == Visibility.Visible ? PwdInput.Password : null;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
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
