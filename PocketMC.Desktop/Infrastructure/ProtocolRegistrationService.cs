using Microsoft.Win32;
using System;
using PocketMC.Desktop.Localization;
using System.Diagnostics;
using System.IO;

namespace PocketMC.Desktop.Infrastructure
{
    public static class ProtocolRegistrationService
    {
        private const string ProtocolScheme = "pocketmc";
        private static readonly string ProtocolDescription = L.T("PocketMC URL Protocol");

        public static void Register()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                var executablePath = global::System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(executablePath))
                {
                    return;
                }

                // Register under HKEY_CURRENT_USER\SOFTWARE\Classes to avoid needing Admin rights
                using var classesKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes", true);
                if (classesKey == null) return;

                using var schemeKey = classesKey.CreateSubKey(ProtocolScheme);
                if (schemeKey == null) return;

                schemeKey.SetValue("", ProtocolDescription);
                schemeKey.SetValue("URL Protocol", "");

                using var defaultIconKey = schemeKey.CreateSubKey("DefaultIcon");
                defaultIconKey?.SetValue("", $"\"{executablePath}\",0");

                using var commandKey = schemeKey.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue("", $"\"{executablePath}\" \"%1\"");
            }
            catch (Exception ex)
            {
                // Ignore errors if we can't write to registry (e.g. no permissions)
                Debug.WriteLine($"Failed to register custom protocol: {ex.Message}");
            }
        }
    }
}
