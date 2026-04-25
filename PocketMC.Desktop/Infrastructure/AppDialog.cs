using System.Windows;

namespace PocketMC.Desktop.Infrastructure
{
    /// <summary>
    /// Static helper that replaces all <see cref="MessageBox.Show"/> calls in the application
    /// with a styled <see cref="AppDialogWindow"/> that matches the app's dark theme.
    /// </summary>
    public static class AppDialog
    {
        /// <summary>Shows an informational dialog with an OK button.</summary>
        public static void ShowInfo(string title, string message)
        {
            Show(title, message, AppDialogType.Info, AppDialogButtons.Ok);
        }

        /// <summary>Shows a warning dialog with an OK button.</summary>
        public static void ShowWarning(string title, string message)
        {
            Show(title, message, AppDialogType.Warning, AppDialogButtons.Ok);
        }

        /// <summary>Shows an error dialog with an OK button.</summary>
        public static void ShowError(string title, string message)
        {
            Show(title, message, AppDialogType.Error, AppDialogButtons.Ok);
        }

        /// <summary>Shows a Yes/No confirmation dialog. Returns true if the user clicked Yes.</summary>
        public static bool Confirm(string title, string message)
        {
            return Show(title, message, AppDialogType.Confirm, AppDialogButtons.YesNo);
        }

        /// <summary>Shows a generic dialog and returns true if the primary button was clicked.</summary>
        public static bool Show(string title, string message, AppDialogType type, AppDialogButtons buttons)
        {
            var dialog = new AppDialogWindow();
            dialog.Configure(title, message, type, buttons);

            // Try to set owner to the main window for proper modality
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null && mainWindow.IsLoaded && mainWindow.IsVisible)
                {
                    dialog.Owner = mainWindow;
                }
            }
            catch
            {
                // Owner assignment can fail during shutdown or startup — continue without it.
            }

            dialog.ShowDialog();
            return dialog.PrimaryClicked;
        }
    }
}
