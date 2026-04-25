using System.Windows;

namespace PocketMC.Desktop.Infrastructure
{
    /// <summary>
    /// Styled FluentWindow used by <see cref="AppDialog"/> to replace all native MessageBox.Show calls.
    /// </summary>
    public partial class AppDialogWindow : Wpf.Ui.Controls.FluentWindow
    {
        /// <summary>True if the user clicked the primary button (OK / Yes).</summary>
        public bool PrimaryClicked { get; private set; }

        public AppDialogWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Configures the dialog for display.
        /// </summary>
        public void Configure(string title, string message, AppDialogType type, AppDialogButtons buttons)
        {
            TxtTitle.Text = title;
            TxtMessage.Text = message;

            // Icon + accent color by type
            switch (type)
            {
                case AppDialogType.Warning:
                    TxtIcon.Text = "⚠";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF)); // yellow
                    TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF));
                    break;

                case AppDialogType.Error:
                    TxtIcon.Text = "✕";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8)); // red
                    TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                    break;

                case AppDialogType.Confirm:
                    TxtIcon.Text = "?";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA)); // blue
                    TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));
                    break;

                case AppDialogType.Info:
                default:
                    TxtIcon.Text = "ℹ";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x89, 0xDC, 0xEB)); // teal
                    TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x89, 0xDC, 0xEB));
                    break;
            }

            // Button configuration
            switch (buttons)
            {
                case AppDialogButtons.OkCancel:
                    BtnPrimary.Content = "OK";
                    BtnSecondary.Content = "Cancel";
                    BtnSecondary.Visibility = Visibility.Visible;
                    break;

                case AppDialogButtons.YesNo:
                    BtnPrimary.Content = "Yes";
                    BtnSecondary.Content = "No";
                    BtnSecondary.Visibility = Visibility.Visible;
                    break;

                case AppDialogButtons.YesNoCancel:
                    BtnPrimary.Content = "Yes";
                    BtnSecondary.Content = "No";
                    BtnSecondary.Visibility = Visibility.Visible;
                    break;

                case AppDialogButtons.Ok:
                default:
                    BtnPrimary.Content = "OK";
                    BtnSecondary.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            PrimaryClicked = true;
            Close();
        }

        private void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            PrimaryClicked = false;
            Close();
        }
    }

    public enum AppDialogType
    {
        Info,
        Warning,
        Error,
        Confirm
    }

    public enum AppDialogButtons
    {
        Ok,
        OkCancel,
        YesNo,
        YesNoCancel
    }
}
