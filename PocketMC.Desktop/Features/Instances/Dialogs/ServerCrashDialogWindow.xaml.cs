using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using PocketMC.Domain.Models;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Instances.Dialogs;

public partial class ServerCrashDialogWindow : FluentWindow
{
    private string? _crashReportPath;
    private string _clipboardPayload = string.Empty;
    private Action? _onOpenConsole;

    public ServerCrashDialogWindow()
    {
        InitializeComponent();
    }

    public void Populate(
        string serverName,
        string serverType,
        string mcVersion,
        CrashAnalysisResult analysis,
        Action? onOpenConsole = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        _onOpenConsole = onOpenConsole;
        _crashReportPath = analysis.CrashReportPath;

        TxtServerName.Text = string.IsNullOrWhiteSpace(serverName) ? "Server" : serverName;
        TxtServerDetails.Text = $"{serverType} • {mcVersion}";

        // Category Badge
        TxtCategoryBadge.Text = analysis.Category switch
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

        TxtCrashTitle.Text = string.IsNullOrWhiteSpace(analysis.Title) ? "Server Crash Detected" : analysis.Title;
        TxtCrashSummary.Text = string.IsNullOrWhiteSpace(analysis.Summary) ? "The server terminated unexpectedly." : analysis.Summary;

        // Log viewer text
        string logContent = !string.IsNullOrWhiteSpace(analysis.FullLogContext)
            ? analysis.FullLogContext
            : (!string.IsNullOrWhiteSpace(analysis.Summary) ? analysis.Summary : "No detailed crash logs available.");
        TxtLogViewer.Text = logContent;

        // Open Crash Report Button Visibility
        if (!string.IsNullOrWhiteSpace(_crashReportPath) && File.Exists(_crashReportPath))
        {
            BtnOpenReport.Visibility = Visibility.Visible;
        }
        else
        {
            BtnOpenReport.Visibility = Visibility.Collapsed;
        }

        // Prepare structured clipboard payload
        var sb = new StringBuilder();
        sb.AppendLine($"=== PocketMC Server Crash Report ===");
        sb.AppendLine($"Server: {serverName} ({serverType} {mcVersion})");
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Category: {TxtCategoryBadge.Text}");
        sb.AppendLine($"Title: {TxtCrashTitle.Text}");
        sb.AppendLine($"Summary: {TxtCrashSummary.Text}");
        if (!string.IsNullOrWhiteSpace(_crashReportPath))
        {
            sb.AppendLine($"Report File: {_crashReportPath}");
        }
        sb.AppendLine();
        sb.AppendLine("--- Log Context / Exception Trace ---");
        sb.AppendLine(logContent);

        _clipboardPayload = sb.ToString();
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
