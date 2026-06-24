using PocketMC.Desktop.Features.Settings.ViewModels;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class SummaryEmojiFormatterTests
{
    [Fact]
    public void Apply_AddsEmojiPrefixesToKnownSummaryHeadings()
    {
        const string markdown = """
**Minecraft Server Analysis Summary**

### Important Events

### Crashes, Warnings, and Lag Spikes

### Recommendations for Improvement
""";

        string formatted = SummaryEmojiFormatter.Apply(markdown);

        Assert.Contains("**🎮 Minecraft Server Analysis Summary**", formatted);
        Assert.Contains("### 🔑 Important Events", formatted);
        Assert.Contains("### ⚠️ Crashes, Warnings, and Lag Spikes", formatted);
        Assert.Contains("### ✅ Recommendations for Improvement", formatted);
    }

    [Fact]
    public void Apply_DoesNotDuplicateExistingEmojiPrefixes()
    {
        const string markdown = """
**🎮 Minecraft Server Analysis Summary**

### 🔑 Important Events
""";

        string formatted = SummaryEmojiFormatter.Apply(markdown);

        Assert.Equal(markdown.ReplaceLineEndings(), formatted.ReplaceLineEndings());
    }

    [Fact]
    public void ViewSummary_AppliesEmojiPrefixesToExistingSavedSummaries()
    {
        string serverDir = Path.Combine(Path.GetTempPath(), $"pocketmc-summary-{Guid.NewGuid():N}");
        try
        {
            var storage = new SummaryStorageService();
            var summary = new SessionSummary
            {
                ServerName = "oneblock",
                SessionStart = DateTime.UtcNow.AddMinutes(-52),
                SessionEnd = DateTime.UtcNow,
                Duration = TimeSpan.FromMinutes(52),
                Content = """
**Minecraft Server Session Summary**

### Key Events

### Player Activity
""",
                AiProvider = "Test"
            };
            storage.Save(serverDir, summary);

            var viewModel = new SettingsSummariesViewModel(serverDir, storage, new SilentDialogService());
            viewModel.Load(hasApiKey: true);
            viewModel.ViewSummaryCommand.Execute(viewModel.Summaries.Single());

            Assert.Contains("**🎮 Minecraft Server Session Summary**", viewModel.SummaryContent);
            Assert.Contains("### 🔑 Key Events", viewModel.SummaryContent);
            Assert.Contains("### 👥 Player Activity", viewModel.SummaryContent);
        }
        finally
        {
            if (Directory.Exists(serverDir))
                Directory.Delete(serverDir, recursive: true);
        }
    }

    private sealed class SilentDialogService : IDialogService
    {
        public Task<DialogResult> ShowDialogAsync(
            string title,
            string message,
            DialogType type = DialogType.Information,
            bool showCancel = false,
            string? primaryButtonText = null,
            string? secondaryButtonText = null,
            string? cancelButtonText = null)
            => Task.FromResult(DialogResult.Ok);

        public void ShowMessage(string title, string message, DialogType type = DialogType.Information)
        {
        }

        public Task<string?> OpenFolderDialogAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult<string?>(null);

        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult(Array.Empty<string>());
    }
}


