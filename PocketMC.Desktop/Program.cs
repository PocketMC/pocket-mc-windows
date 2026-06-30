using System;
using System.Diagnostics;
using System.IO;
using Velopack;
using Velopack.Windows;

namespace PocketMC.Desktop;

public static class Program
{
    public static System.Windows.SplashScreen? Splash { get; private set; }
    
    private const ShortcutLocation ShortcutRefreshLocations = ShortcutLocation.Desktop | ShortcutLocation.StartMenuRoot;

    [System.Runtime.InteropServices.DllImport("Shell32.dll")]
    private static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private static void RefreshWindowsIconCache(string reason)
    {
        try
        {
            // SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            LogShortcutRefresh($"{reason}: Broadcasted SHChangeNotify to refresh Windows icon cache.");
        }
        catch (Exception ex)
        {
            LogShortcutRefresh($"{reason}: Failed to refresh icon cache. {ex}");
        }
    }

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
            .OnAfterInstallFastCallback((v) => RefreshWindowsIconCache("install"))
            .OnAfterUpdateFastCallback((v) => RefreshWindowsIconCache("update"))
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
            // Show splash screen manually because we have a custom Main method
            // Use autoClose: false so we can close it instantly with 0ms fade later
            Splash = new System.Windows.SplashScreen("assets/splash.png");
            Splash.Show(false, topMost: true);

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
