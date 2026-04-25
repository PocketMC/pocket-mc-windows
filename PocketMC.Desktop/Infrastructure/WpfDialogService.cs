using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class WpfDialogService : IDialogService
    {
        public Task<DialogResult> ShowDialogAsync(string title, string message, DialogType type = DialogType.Information, bool showCancel = false)
        {
            var appType = type switch
            {
                DialogType.Warning => AppDialogType.Warning,
                DialogType.Error => AppDialogType.Error,
                DialogType.Question => AppDialogType.Confirm,
                _ => AppDialogType.Info
            };

            var buttons = showCancel ? AppDialogButtons.YesNoCancel : AppDialogButtons.YesNo;
            bool primary = AppDialog.Show(title, message, appType, buttons);

            return Task.FromResult(primary ? DialogResult.Yes : DialogResult.No);
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
    }
}
