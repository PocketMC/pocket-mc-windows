using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Intelligence
{
    public partial class OllamaModelManagerDialog : FluentWindow
    {
        private readonly IOllamaService _ollamaService;
        private readonly string _endpoint;
        private readonly string? _apiKey;
        private HashSet<string> _installedModelNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _progressTimer;
        private CancellationTokenSource? _pullCts;
        private LatestPullProgress? _activeProgress;

        private readonly bool _isCloud;

        public string? SelectedModelName { get; private set; }

        public OllamaModelManagerDialog(IOllamaService ollamaService, string endpoint, string? apiKey)
        {
            InitializeComponent();
            var visualService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(
                    ((App)System.Windows.Application.Current).Services);
            visualService.ApplyThemeToDialog(this);

            _ollamaService = ollamaService;
            _endpoint = endpoint;
            _apiKey = apiKey;
            _isCloud = !string.IsNullOrWhiteSpace(endpoint) && endpoint.Contains("ollama.com", StringComparison.OrdinalIgnoreCase);
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _progressTimer.Tick += (_, _) =>
            {
                if (_activeProgress?.Latest is { } latestProgress)
                    UpdateProgressDisplay(latestProgress);
            };
            Closing += (_, _) =>
            {
                _progressTimer.Stop();
                _pullCts?.Cancel();
            };

            TabInstalledModels.IsChecked = true;

            if (_isCloud)
            {
                Title = "Ollama Cloud Models";
                TabInstalledModels.Content = "Cloud Models";
                TabDownloadModels.Visibility = Visibility.Collapsed;
                TabManualSteps.Visibility = Visibility.Collapsed;
                DownloadTabColumn.Width = new GridLength(0);
                ManualTabColumn.Width = new GridLength(0);
            }

            Loaded += OllamaModelManagerDialog_Loaded;
        }

        private async void OllamaModelManagerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadInstalledModelsAsync();
        }

        private void ManagerTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton tab || tab.Tag is not string selectedTab)
                return;

            InstalledTabPanel.Visibility = selectedTab == "Installed" ? Visibility.Visible : Visibility.Collapsed;
            DownloadTabPanel.Visibility = selectedTab == "Download" ? Visibility.Visible : Visibility.Collapsed;
            ManualTabPanel.Visibility = selectedTab == "Manual" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadInstalledModelsAsync()
        {
            try
            {
                StatusText.Text = "Loading models...";
                var models = await _ollamaService.GetInstalledModelsAsync(_endpoint, _apiKey);
                _installedModelNames = models
                    .Select(model => model.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var viewModels = models.Select(m => new InstalledModelViewModel(m)).ToList();
                InstalledModelsList.ItemsSource = viewModels;
                UpdateRecommendedModelVisibility();

                StatusText.Text = viewModels.Count > 0
                    ? $"{viewModels.Count} model(s) available."
                    : (_isCloud ? "No cloud models found. Check your API key or connection." : "No models found. Use the Download tab to pull models.");
            }
            catch (Exception ex)
            {
                _installedModelNames.Clear();
                StatusText.Text = $"Error: {ex.Message}";
                InstalledModelsList.ItemsSource = null;
                UpdateRecommendedModelVisibility();
            }
        }

        private void UpdateRecommendedModelVisibility()
        {
            RecommendedLlamaCard.Visibility = IsRecommendedModelInstalled("llama3.2:3b") ? Visibility.Collapsed : Visibility.Visible;
            RecommendedQwenCard.Visibility = IsRecommendedModelInstalled("qwen2.5:7b") ? Visibility.Collapsed : Visibility.Visible;
            RecommendedDeepseekCard.Visibility = IsRecommendedModelInstalled("deepseek-r1:8b") ? Visibility.Collapsed : Visibility.Visible;
            RecommendedPhiCard.Visibility = IsRecommendedModelInstalled("phi4:14b") ? Visibility.Collapsed : Visibility.Visible;
            RecommendedMistralCard.Visibility = IsRecommendedModelInstalled("mistral:7b") ? Visibility.Collapsed : Visibility.Visible;

            bool hasRecommendedModels = !IsRecommendedModelInstalled("llama3.2:3b") ||
                !IsRecommendedModelInstalled("qwen2.5:7b") ||
                !IsRecommendedModelInstalled("deepseek-r1:8b") ||
                !IsRecommendedModelInstalled("phi4:14b") ||
                !IsRecommendedModelInstalled("mistral:7b");
            RecommendedModelsHeader.Visibility = hasRecommendedModels ? Visibility.Visible : Visibility.Collapsed;
            NoRecommendedModelsText.Visibility = hasRecommendedModels ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool IsRecommendedModelInstalled(string modelName) => _installedModelNames.Contains(modelName);

        private void SelectModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string modelName)
            {
                SelectedModelName = modelName;
                DialogResult = true;
                Close();
            }
        }

        private async void DeleteModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button button || button.Tag is not string modelName)
                return;

            if (!AppDialog.Confirm(
                    "Delete Ollama Model",
                    $"Permanently delete '{modelName}' from Ollama's local model storage?"))
                return;

            button.IsEnabled = false;
            StatusText.Text = $"Deleting {modelName}...";
            try
            {
                await _ollamaService.DeleteModelAsync(_endpoint, modelName, _apiKey);
                await LoadInstalledModelsAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not delete {modelName}.";
                AppDialog.ShowError("Delete Model Failed", ex.Message);
            }
            finally
            {
                if (button.IsLoaded)
                    button.IsEnabled = true;
            }
        }

        private async void DownloadRecommended_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string modelName)
            {
                await PullModelAsync(modelName);
            }
        }

        private async void PullCustomModel_Click(object sender, RoutedEventArgs e)
        {
            var modelName = CustomModelTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(modelName))
            {
                await PullModelAsync(modelName);
            }
        }

        private async Task PullModelAsync(string modelName)
        {
            if (_pullCts != null || !IsLoaded)
                return;

            var pullCts = new CancellationTokenSource();
            _pullCts = pullCts;
            var pullProgress = new LatestPullProgress();
            _activeProgress = pullProgress;
            ProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressBar.Value = 0;
            ProgressStatusText.Text = $"Starting download: {modelName}...";
            _progressTimer.Start();

            try
            {
                await _ollamaService.PullModelAsync(_endpoint, modelName, _apiKey, pullProgress, pullCts.Token);
                if (!string.IsNullOrWhiteSpace(pullProgress.Latest?.ErrorMessage))
                    throw new InvalidOperationException(pullProgress.Latest.ErrorMessage);

                if (!IsLoaded)
                    return;

                ProgressStatusText.Text = "Download complete.";
                DownloadProgressBar.Value = 100;
                await LoadInstalledModelsAsync();
            }
            catch (OperationCanceledException)
            {
                if (IsLoaded)
                    ProgressStatusText.Text = "Download cancelled.";
            }
            catch (Exception ex)
            {
                if (IsLoaded)
                    ProgressStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _progressTimer.Stop();
                _activeProgress = null;
                if (ReferenceEquals(_pullCts, pullCts))
                    _pullCts = null;
                pullCts.Dispose();
                if (IsLoaded)
                    ProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateProgressDisplay(OllamaPullProgress progress)
        {
            if (!string.IsNullOrEmpty(progress.ErrorMessage))
            {
                ProgressStatusText.Text = $"Error: {progress.ErrorMessage}";
                return;
            }

            if (progress.Percent is double percent)
            {
                DownloadProgressBar.Value = Math.Clamp(percent, 0, 100);
                var completedMb = (progress.CompletedBytes ?? 0) / (1024.0 * 1024.0);
                var totalMb = (progress.TotalBytes ?? 0) / (1024.0 * 1024.0);
                ProgressStatusText.Text = $"{progress.Status} - {percent:F1}% ({completedMb:F0} MB / {totalMb:F0} MB)";
            }
            else
            {
                ProgressStatusText.Text = progress.Status;
            }
        }

        private void CancelPull_Click(object sender, RoutedEventArgs e)
        {
            _pullCts?.Cancel();
        }

        private void CopyCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ManualCommandBox.Text);
            }
            catch
            {
                // Clipboard access can fail if locked by another process
            }
        }
    }

    internal sealed class LatestPullProgress : IProgress<OllamaPullProgress>
    {
        private OllamaPullProgress? _latest;

        public OllamaPullProgress? Latest => Volatile.Read(ref _latest);

        public void Report(OllamaPullProgress value)
        {
            Volatile.Write(ref _latest, value);
        }
    }

    public class InstalledModelViewModel
    {
        public string Name { get; }
        public string SizeText { get; }
        public string? ParameterSize { get; }
        public string? QuantizationLevel { get; }
        public string DetailsSummary { get; }
        public IReadOnlyList<string> CapabilityList { get; }
        public bool IsEmbeddingOnly { get; }
        public Visibility DeleteVisibility { get; }

        public InstalledModelViewModel(OllamaModelInfo model)
        {
            Name = model.Name;
            SizeText = model.FormattedSize;
            ParameterSize = model.ParameterSize;
            QuantizationLevel = model.QuantizationLevel;
            IsEmbeddingOnly = !model.SupportsCompletion;
            DeleteVisibility = model.IsCloud ? Visibility.Collapsed : Visibility.Visible;

            var parts = new List<string>();
            if (model.IsCloud)
                parts.Add("Ollama Cloud");
            if (!string.IsNullOrWhiteSpace(model.ParameterSize))
                parts.Add($"Params: {model.ParameterSize}");
            if (!string.IsNullOrWhiteSpace(model.FormattedSize) && model.FormattedSize != "Unknown")
                parts.Add($"Size: {model.FormattedSize}");
            if (!string.IsNullOrWhiteSpace(model.QuantizationLevel))
                parts.Add($"Quant: {model.QuantizationLevel}");

            DetailsSummary = parts.Count > 0
                ? string.Join("  |  ", parts)
                : (model.IsCloud ? "Cloud Model" : "Local Model");

            var caps = new List<string>();
            foreach (var cap in model.Capabilities)
            {
                var display = char.ToUpper(cap[0]) + cap[1..];
                caps.Add(display);
            }
            if (caps.Count == 0 && model.SupportsCompletion)
                caps.Add("Completion");
            if (IsEmbeddingOnly)
                caps.Add("Embedding Only");

            CapabilityList = caps;
        }
    }
}
