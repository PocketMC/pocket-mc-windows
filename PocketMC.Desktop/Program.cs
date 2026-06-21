using System;
using System.Runtime.InteropServices;
using System.Windows;
using Velopack;
using Velopack.Windows;

namespace PocketMC.Desktop;

public static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0;

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack MUST be bootstrapped before any WPF code runs.
        // This handles squirrel-style install/uninstall hooks and
        // delta-patch application on startup.
        VelopackApp.Build()
            .WithVersionApplied((v) => 
            {
                // Re-create shortcuts to point to the new icon
                try 
                {
                    new Shortcuts().CreateShortcutForThisExe(ShortcutLocation.Desktop | ShortcutLocation.StartMenu);
                } 
                catch { /* Ignore errors */ }

                // Tell the Windows Shell to flush and reload its icon caches
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            })
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
