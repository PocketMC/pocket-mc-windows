using PocketMC.Desktop.Core.Interfaces;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class WpfDialogService : IDialogService
    {
        public Task<DialogResult> ShowDialogAsync(
            string title,
            string message,
            DialogType type = DialogType.Information,
            bool showCancel = false,
            string? primaryButtonText = null,
            string? secondaryButtonText = null,
            string? cancelButtonText = null,
            string? linkText = null,
            string? linkUrl = null)
        {
            var appType = type switch
            {
                DialogType.Warning => AppDialogType.Warning,
                DialogType.Error => AppDialogType.Error,
                DialogType.Question => AppDialogType.Confirm,
                _ => AppDialogType.Info
            };

            // Only use the 3-button layout if a specific text for the third button is provided.
            // This prevents redundant "No" and "Cancel" buttons from appearing together for simple confirmations.
            var buttons = (showCancel && !string.IsNullOrEmpty(cancelButtonText))
                ? AppDialogButtons.YesNoCancel
                : AppDialogButtons.YesNo;
            DialogResult result = AppDialog.ShowResult(
                title,
                message,
                appType,
                buttons,
                primaryButtonText,
                secondaryButtonText,
                cancelButtonText,
                linkText,
                linkUrl);

            return Task.FromResult(result);
        }

        public void ShowMessage(string title, string message, DialogType type = DialogType.Information)
        {
            var appType = type switch
            {
                DialogType.Warning => AppDialogType.Warning,
                DialogType.Error => AppDialogType.Error,
                DialogType.Question => AppDialogType.Confirm,
                _ => AppDialogType.Info
            };

            AppDialog.Show(title, message, appType, AppDialogButtons.Ok);
        }

        public Task<string?> OpenFolderDialogAsync(string title)
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
        }

        public Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*")
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
        }

        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*")
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileNames : System.Array.Empty<string>());
        }

        public Task<string?> PromptPasswordAsync(string title, string message)
        {
            var tcs = new TaskCompletionSource<string?>();
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new PasswordPromptDialogWindow(title, message)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                if (dialog.ShowDialog() == true)
                {
                    tcs.SetResult(dialog.Password);
                }
                else
                {
                    tcs.SetResult(null);
                }
            });
            return tcs.Task;
        }
        public Task ShowProgressDialogAsync(string title, string message, System.Func<System.IProgress<double>, Task> action)
        {
            var tcs = new TaskCompletionSource();
            System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var dialog = new ProgressDialogWindow(title, message, action)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    dialog.ShowDialog(); // This blocks the UI thread until the action is complete and closes itself
                    tcs.SetResult();
                }
                catch (System.Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
