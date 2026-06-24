using System.Threading.Tasks;

namespace PocketMC.Desktop.Core.Interfaces
{
    public enum DialogResult { Ok, Cancel, Yes, No, Dismiss }
    public enum DialogType { Information, Warning, Error, Question }

    public interface IDialogService
    {
        Task<DialogResult> ShowDialogAsync(
            string title,
            string message,
            DialogType type = DialogType.Information,
            bool showCancel = false,
            string? primaryButtonText = null,
            string? secondaryButtonText = null,
            string? cancelButtonText = null);
        void ShowMessage(string title, string message, DialogType type = DialogType.Information);
        Task<string?> OpenFolderDialogAsync(string title);
        Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*");
        Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*");
        Task<string?> PromptPasswordAsync(string title, string message);
    }
}
