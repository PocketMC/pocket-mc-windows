using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Instances.Dialogs;

public partial class ServerCrashDialogWindow : FluentWindow
{
    private string? _crashReportPath;
    private string _clipboardPayload = string.Empty;
    private Action? _onOpenConsole;
    private ApplicationState? _appState;
    private ILlmProviderFactory? _providerFactory;

    private string _serverName = string.Empty;
    private string _serverType = string.Empty;
    private string _mcVersion = string.Empty;
    private string _categoryText = string.Empty;
    private string _summaryText = string.Empty;
    private string _rawLogContent = string.Empty;
    private string? _aiAnalysisResult;
    private bool _isShowingAiView;

    public ServerCrashDialogWindow()
    {
        InitializeComponent();
    }

    public void Populate(
        string serverName,
        string serverType,
        string mcVersion,
        CrashAnalysisResult analysis,
        ApplicationState? appState = null,
        ILlmProviderFactory? providerFactory = null,
        Action? onOpenConsole = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        _appState = appState;
        _providerFactory = providerFactory;
        _onOpenConsole = onOpenConsole;
        _crashReportPath = analysis.CrashReportPath;
        _serverName = string.IsNullOrWhiteSpace(serverName) ? "Server" : serverName;
        _serverType = serverType;
        _mcVersion = mcVersion;
        _summaryText = string.IsNullOrWhiteSpace(analysis.Summary) ? "The server terminated unexpectedly." : analysis.Summary;

        TxtServerName.Text = _serverName;
        TxtServerDetails.Text = $"{serverType} • {mcVersion}";

        // Category Badge
        _categoryText = analysis.Category switch
        {
            CrashCategory.ModDependency => "Mod Dependency",
            CrashCategory.MissingMod => "Missing Mod",
            CrashCategory.MixinConflict => "Mixin Collision",
            CrashCategory.OutOfMemory => "Out of Memory",
            CrashCategory.JavaRuntime => "Java Exception",
            CrashCategory.BedrockScript => "Bedrock Error",
            CrashCategory.PocketMineFatal => "PocketMine Crash",
            CrashCategory.ServerTickException => "Crash Report",
            CrashCategory.StartupAborted => "Startup Failed",
            CrashCategory.ProcessExitError => $"Exit Code {analysis.ExitCode}",
            _ => "Crashed"
        };
        TxtCategoryBadge.Text = _categoryText;

        TxtCrashTitle.Text = string.IsNullOrWhiteSpace(analysis.Title) ? "Server Crash Detected" : analysis.Title;
        TxtCrashSummary.Text = _summaryText;

        // Log viewer text
        _rawLogContent = !string.IsNullOrWhiteSpace(analysis.FullLogContext)
            ? analysis.FullLogContext
            : (!string.IsNullOrWhiteSpace(analysis.Summary) ? analysis.Summary : "No detailed crash logs available.");
        TxtLogViewer.Text = _rawLogContent;

        // Open Crash Report Button Visibility
        if (!string.IsNullOrWhiteSpace(_crashReportPath) && File.Exists(_crashReportPath))
        {
            BtnOpenReport.Visibility = Visibility.Visible;
        }
        else
        {
            BtnOpenReport.Visibility = Visibility.Collapsed;
        }

        UpdateClipboardPayload();
    }

    private void UpdateClipboardPayload()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== PocketMC Server Crash Report ===");
        sb.AppendLine($"Server: {_serverName} ({_serverType} {_mcVersion})");
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Category: {_categoryText}");
        sb.AppendLine($"Title: {TxtCrashTitle.Text}");
        sb.AppendLine($"Summary: {_summaryText}");
        if (!string.IsNullOrWhiteSpace(_crashReportPath))
        {
            sb.AppendLine($"Report File: {_crashReportPath}");
        }

        if (!string.IsNullOrWhiteSpace(_aiAnalysisResult))
        {
            sb.AppendLine();
            sb.AppendLine("--- AI Diagnostic Analysis ---");
            sb.AppendLine(_aiAnalysisResult);
        }

        sb.AppendLine();
        sb.AppendLine("--- Diagnostic Log & Trace ---");
        sb.AppendLine(_rawLogContent);

        _clipboardPayload = sb.ToString();
    }

    private async void BtnAnalyzeWithAi_Click(object sender, RoutedEventArgs e)
    {
        var appSettings = _appState?.Settings;
        if (appSettings == null)
        {
            ShowAiContainer();
            AiMarkdownViewer.Markdown = "> [!WARNING]\n> **Application Settings Unavailable**\n>\n> PocketMC settings could not be accessed.";
            BtnToggleView.Visibility = Visibility.Visible;
            BtnToggleView.Content = "View Logs";
            _isShowingAiView = true;
            return;
        }

        var apiKey = appSettings.GetCurrentAiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowAiContainer();
            AiMarkdownViewer.Markdown = "> [!WARNING]\n> **AI Provider Not Configured**\n>\n> No API key was found for the configured AI provider (**" + (appSettings.AiProvider ?? "AI") + "**).\n>\n> Please open **App Settings > AI Intelligence** to enter your API key.";
            BtnToggleView.Visibility = Visibility.Visible;
            BtnToggleView.Content = "View Logs";
            _isShowingAiView = true;
            return;
        }

        if (_providerFactory == null)
        {
            ShowAiContainer();
            AiMarkdownViewer.Markdown = "> [!WARNING]\n> **AI Service Unavailable**\n>\n> PocketMC intelligence service is currently not available.";
            BtnToggleView.Visibility = Visibility.Visible;
            BtnToggleView.Content = "View Logs";
            _isShowingAiView = true;
            return;
        }

        // Show loading state
        LogContainer.Visibility = Visibility.Collapsed;
        AiMarkdownContainer.Visibility = Visibility.Collapsed;
        AiLoadingContainer.Visibility = Visibility.Visible;
        TxtLogSectionTitle.Text = "AI Crash Intelligence";
        BtnToggleView.Visibility = Visibility.Collapsed;
        BtnAnalyzeWithAi.IsEnabled = false;

        string providerName = appSettings.AiProvider ?? "AI Provider";
        string modelName = appSettings.GetCurrentAiModel() ?? "default model";
        TxtAiProviderStatus.Text = $"Consulting {providerName} ({modelName}) to diagnose root cause and solution...";

        try
        {
            var providerType = _providerFactory.ParseProvider(providerName);
            var provider = _providerFactory.GetProvider(providerType);
            string endpoint = appSettings.GetCurrentAiEndpoint() ?? "";

            string systemPrompt = @"You are an expert Minecraft Server Engineer and Crash Diagnostic Specialist.
Analyze the provided Minecraft server crash log or diagnostic report.

Provide a concise, clear, and structured diagnosis in Markdown:
### 1. Root Cause
Explain clearly and concisely why the server crashed (specify offending mod ID, filename, Java mismatch, memory error, or corrupted data if detected).

### 2. Recommended Fix
Provide exact, numbered, step-by-step instructions for the user to resolve this issue (e.g. download required dependency version, remove conflicting mod, adjust memory/JVM arguments, etc.).";

            string userContent = $"Server Name: {_serverName}\nServer Engine: {_serverType} {_mcVersion}\nCrash Category: {_categoryText}\nCrash Summary: {_summaryText}\n\nCrash Log Context:\n{_rawLogContent}";

            var result = await provider.GenerateCompletionAsync(apiKey, modelName, endpoint, systemPrompt, userContent);

            if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
            {
                _aiAnalysisResult = result.Content;
                AiMarkdownViewer.Markdown = result.Content;
                UpdateClipboardPayload();
            }
            else
            {
                string errorMsg = !string.IsNullOrWhiteSpace(result.Error) ? result.Error : "AI provider did not return a response.";
                AiMarkdownViewer.Markdown = $"> [!CAUTION]\n> **AI Analysis Failure**\n>\n> {errorMsg}";
            }
        }
        catch (Exception ex)
        {
            AiMarkdownViewer.Markdown = $"> [!CAUTION]\n> **AI Analysis Error**\n>\n> {ex.Message}";
        }
        finally
        {
            AiLoadingContainer.Visibility = Visibility.Collapsed;
            AiMarkdownContainer.Visibility = Visibility.Visible;
            BtnAnalyzeWithAi.IsEnabled = true;
            BtnAnalyzeWithAi.Content = "Re-analyze with AI";
            BtnToggleView.Visibility = Visibility.Visible;
            BtnToggleView.Content = "View Logs";
            _isShowingAiView = true;
        }
    }

    private void ShowAiContainer()
    {
        LogContainer.Visibility = Visibility.Collapsed;
        AiLoadingContainer.Visibility = Visibility.Collapsed;
        AiMarkdownContainer.Visibility = Visibility.Visible;
        TxtLogSectionTitle.Text = "AI Crash Intelligence";
    }

    private void BtnToggleView_Click(object sender, RoutedEventArgs e)
    {
        if (_isShowingAiView)
        {
            // Switch to Logs
            AiMarkdownContainer.Visibility = Visibility.Collapsed;
            AiLoadingContainer.Visibility = Visibility.Collapsed;
            LogContainer.Visibility = Visibility.Visible;
            TxtLogSectionTitle.Text = "Diagnostic & Exception Log";
            BtnToggleView.Content = "View AI Analysis";
            _isShowingAiView = false;
        }
        else
        {
            // Switch to AI
            LogContainer.Visibility = Visibility.Collapsed;
            AiLoadingContainer.Visibility = Visibility.Collapsed;
            AiMarkdownContainer.Visibility = Visibility.Visible;
            TxtLogSectionTitle.Text = "AI Crash Intelligence";
            BtnToggleView.Content = "View Logs";
            _isShowingAiView = true;
        }
    }

    private async void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_clipboardPayload);
            string originalText = BtnCopyLogs.Content?.ToString() ?? "Copy Crash Logs";
            BtnCopyLogs.Content = "Copied to Clipboard!";
            await Task.Delay(1800);
            BtnCopyLogs.Content = originalText;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to copy crash logs: {ex.Message}");
        }
    }

    private void BtnOpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_crashReportPath) || !File.Exists(_crashReportPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _crashReportPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open crash report: {ex.Message}");
        }
    }

    private void BtnConsole_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onOpenConsole?.Invoke();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
