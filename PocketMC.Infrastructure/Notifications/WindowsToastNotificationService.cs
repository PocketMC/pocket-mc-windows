using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using PocketMC.Application.Interfaces;

namespace PocketMC.Infrastructure;

public sealed class WindowsToastNotificationService : INotificationService
{
    private const string AppUserModelId = "PocketMC.Desktop";
    private static bool _isRegistered;
    private readonly ILogger<WindowsToastNotificationService> _logger;

    public static event Action<Guid>? OnSummaryNotificationClicked;

    public WindowsToastNotificationService(ILogger<WindowsToastNotificationService> logger)
    {
        _logger = logger;
    }

    public static void RegisterApplication()
    {
        if (_isRegistered) return;

        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            try
            {
                var argString = (toastArgs.Argument ?? string.Empty).Replace(";", "&");
                var query = System.Web.HttpUtility.ParseQueryString(argString);
                if (query["action"] == "openSummary")
                {
                    var instanceIdStr = query["instanceId"];
                    if (Guid.TryParse(instanceIdStr, out var instanceId))
                    {
                        OnSummaryNotificationClicked?.Invoke(instanceId);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore activation errors
                System.Diagnostics.Debug.WriteLine($"PocketMC toast activation handler failed: {ex}");
            }
        };
        _isRegistered = true;
    }

    public void ShowAgentConnected()
    {
        ShowToast("Agent connected", "Your Playit Agent is Ready.");
    }

    public void ShowTunnelCreated(int serverPort, string address)
    {
        ShowToast("Tunnel created", $"Port {serverPort} is now publicly accessible on {address}. You can now close the browser window.");
    }

    public void ShowInformation(string title, string message)
    {
        ShowToast(title, message);
    }

    public void ShowServerOnline(string serverName, string version, string loaderType)
    {
        ShowToast("Server Online", $"{serverName} ({loaderType} {version}) is now online.");
    }

    public void ShowSummaryComplete(string instanceId, string serverName)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "openSummary")
                .AddArgument("instanceId", instanceId)
                .AddText("AI Summary Complete")
                .AddText($"Session summary saved for '{serverName}'.")
                .AddButton(new ToastButton()
                    .SetContent("View")
                    .AddArgument("action", "openSummary")
                    .AddArgument("instanceId", instanceId))
                .Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show Windows toast notification for AI summary.");
        }
    }

    public void ShowRemoteControlStarted()
    {
        ShowToast("Remote Control", "Your remote control web panel is started.");
    }

    private void ShowToast(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show Windows toast notification '{Title}'.", title);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}


