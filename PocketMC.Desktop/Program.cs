using System;
using System.Diagnostics;
using System.IO;
using Velopack;
using Velopack.Windows;

namespace PocketMC.Desktop;

public static class Program
{
    private const ShortcutLocation ShortcutRefreshLocations = ShortcutLocation.Desktop | ShortcutLocation.StartMenuRoot;

#pragma warning disable CS0618 // Velopack shortcut helpers are required here to explicitly refresh shortcuts after install/update.
    private static void RecreateShortcuts(string reason)
    {
        try
        {
            var shortcuts = new Shortcuts();
            var currentExeName = Path.GetFileName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.FriendlyName;
            var existingShortcuts = shortcuts.FindShortcuts(currentExeName, ShortcutRefreshLocations);

            foreach (var location in new[] { ShortcutLocation.Desktop, ShortcutLocation.StartMenuRoot })
            {
                LogShortcutRefresh($"{reason}: Pocket MC shortcut {(existingShortcuts.ContainsKey(location) ? "exists" : "does not exist")} at {location}.");
            }

            shortcuts.DeleteShortcuts(currentExeName, ShortcutRefreshLocations);
            LogShortcutRefresh($"{reason}: Deleted existing Pocket MC shortcuts from Desktop and Start Menu locations.");

            shortcuts.CreateShortcutForThisExe(ShortcutRefreshLocations);
            LogShortcutRefresh($"{reason}: Recreated Pocket MC shortcuts for current executable in Desktop and Start Menu locations.");
        }
        catch (Exception ex)
        {
            LogShortcutRefresh($"{reason}: Shortcut refresh failed but install/update will continue. {ex}");
        }
    }
#pragma warning restore CS0618

    private static void LogShortcutRefresh(string message)
    {
        var formattedMessage = $"[{DateTimeOffset.Now:O}] [PocketMC Shortcut Refresh] {message}";
        Trace.TraceInformation(formattedMessage);
        Console.Error.WriteLine(formattedMessage);

        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketMC");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "shortcut-refresh.log"), formattedMessage + Environment.NewLine);
        }
        catch
        {
            // Shortcut refresh logging must never block install or update completion.
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack MUST be bootstrapped before any WPF code runs.
        // This handles squirrel-style install/uninstall hooks and
        // delta-patch application on startup.
        VelopackApp.Build()
            .OnAfterInstallFastCallback((v) => RecreateShortcuts("install"))
            .OnAfterUpdateFastCallback((v) => RecreateShortcuts("update"))
            .Run();

        // Enforce single instance rule before starting WPF
        if (!Infrastructure.SingleInstanceService.InitializeAsFirstInstance(args))
        {
            // Another instance is running, and we just sent it a message to show itself.
            // Exit immediately.
            return;
        }

        try
        {
            // Normal WPF startup
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        finally
        {
            Infrastructure.SingleInstanceService.Cleanup();
        }
    }
}
