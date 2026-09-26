using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Intelligence
{
    public partial class OllamaModelManagerDialog : FluentWindow
    {
        private readonly IOllamaService _ollamaService;
        private readonly string _endpoint;
        private readonly string? _apiKey;
        private CancellationTokenSource? _pullCts;

        private readonly bool _isCloud;

        public string? SelectedModelName { get; private set; }

        public OllamaModelManagerDialog(IOllamaService ollamaService, string endpoint, string? apiKey)
        {
            InitializeComponent();
            _ollamaService = ollamaService;
            _endpoint = endpoint;
            _apiKey = apiKey;
            _isCloud = !string.IsNullOrWhiteSpace(endpoint) && endpoint.Contains("ollama.com", StringComparison.OrdinalIgnoreCase);

            if (_isCloud)
            {
                Title = "Ollama Cloud Models";
                TabInstalledModels.Header = "Cloud Models";
                TabDownloadModels.Visibility = Visibility.Collapsed;
                TabManualSteps.Visibility = Visibility.Collapsed;
            }

            Loaded += OllamaModelManagerDialog_Loaded;
        }

        private async void OllamaModelManagerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadInstalledModelsAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadInstalledModelsAsync();
        }

        private async Task LoadInstalledModelsAsync()
        {
            try
            {
                StatusText.Text = "Loading models...";
                var models = await _ollamaService.GetInstalledModelsAsync(_endpoint, _apiKey);

                var viewModels = models.Select(m => new InstalledModelViewModel(m)).ToList();
                InstalledModelsList.ItemsSource = viewModels;

                StatusText.Text = viewModels.Count > 0
                    ? $"{viewModels.Count} model(s) available."
                    : (_isCloud ? "No cloud models found. Check your API key or connection." : "No models found. Use the Download tab to pull models.");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                InstalledModelsList.ItemsSource = null;
            }
        }

        private void SelectModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string modelName)
            {
                SelectedModelName = modelName;
                DialogResult = true;
                Close();
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
            if (_pullCts != null)
                return;

            _pullCts = new CancellationTokenSource();
            ProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressBar.Value = 0;
            ProgressStatusText.Text = $"Starting download: {modelName}...";

            var progress = new Progress<OllamaPullProgress>(p =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(p.ErrorMessage))
                    {
                        ProgressStatusText.Text = $"Error: {p.ErrorMessage}";
                        return;
                    }

                    if (p.Percent.HasValue)
                    {
                        DownloadProgressBar.Value = p.Percent.Value;
                        var completedMb = (p.CompletedBytes ?? 0) / (1024.0 * 1024.0);
                        var totalMb = (p.TotalBytes ?? 0) / (1024.0 * 1024.0);
                        ProgressStatusText.Text = $"{p.Status} - {p.Percent.Value:F1}% ({completedMb:F0} MB / {totalMb:F0} MB)";
                    }
                    else
                    {
                        ProgressStatusText.Text = p.Status;
                    }
                });
            });

            try
            {
                await _ollamaService.PullModelAsync(_endpoint, modelName, _apiKey, progress, _pullCts.Token);
                ProgressStatusText.Text = "Download complete.";
                DownloadProgressBar.Value = 100;
                await Task.Delay(1000);
                await LoadInstalledModelsAsync();
            }
            catch (OperationCanceledException)
            {
                ProgressStatusText.Text = "Download cancelled.";
            }
            catch (Exception ex)
            {
                ProgressStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _pullCts?.Dispose();
                _pullCts = null;
                ProgressPanel.Visibility = Visibility.Collapsed;
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

    public class InstalledModelViewModel
    {
        public string Name { get; }
        public string SizeText { get; }
        public string? ParameterSize { get; }
        public string? QuantizationLevel { get; }
        public string DetailsSummary { get; }
        public IReadOnlyList<string> CapabilityList { get; }
        public bool IsEmbeddingOnly { get; }

        public InstalledModelViewModel(OllamaModelInfo model)
        {
            Name = model.Name;
            SizeText = model.FormattedSize;
            ParameterSize = model.ParameterSize;
            QuantizationLevel = model.QuantizationLevel;
            IsEmbeddingOnly = !model.SupportsCompletion;

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
