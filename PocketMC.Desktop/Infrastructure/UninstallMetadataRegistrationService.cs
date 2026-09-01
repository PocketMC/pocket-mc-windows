using Microsoft.Win32;
using PocketMC.Infrastructure.Configuration;
using System;
using System.Diagnostics;

namespace PocketMC.Desktop.Infrastructure
{
    public static class UninstallMetadataRegistrationService
    {
        public static void Sync()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                var subKeyName = string.IsNullOrWhiteSpace(AppConfig.AppId) ? "PocketMC" : AppConfig.AppId;
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{subKeyName}", writable: true);
                if (key != null)
                {
                    if (!string.IsNullOrWhiteSpace(AppConfig.OrganizationName))
                    {
                        key.SetValue("Publisher", AppConfig.OrganizationName);
                    }

                    if (!string.IsNullOrWhiteSpace(AppConfig.LinkGitHub))
                    {
                        key.SetValue("URLInfoAbout", AppConfig.LinkGitHub);
                    }

                    if (!string.IsNullOrWhiteSpace(AppConfig.LinkReleases))
                    {
                        key.SetValue("URLUpdateInfo", AppConfig.LinkReleases);
                    }

                    if (!string.IsNullOrWhiteSpace(AppConfig.LinkDiscord))
                    {
                        key.SetValue("HelpLink", AppConfig.LinkDiscord);
                    }

                    if (!string.IsNullOrWhiteSpace(AppConfig.AppDescription))
                    {
                        key.SetValue("Comments", AppConfig.AppDescription);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync uninstall metadata: {ex.Message}");
            }
        }
    }
}
