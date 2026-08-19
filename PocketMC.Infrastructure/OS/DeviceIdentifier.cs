using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PocketMC.Infrastructure.OS
{
    public static class DeviceIdentifier
    {
        private static string? _cachedMachineId;
        private static readonly object _lock = new();

        /// <summary>
        /// Retrieves a stable, anonymous, and deterministic Machine ID unique to this physical Windows device.
        /// Does not change across app updates, settings restores, or settings.json resets.
        /// </summary>
        public static string GetMachineId()
        {
            if (_cachedMachineId != null)
            {
                return _cachedMachineId;
            }

            lock (_lock)
            {
                if (_cachedMachineId != null)
                {
                    return _cachedMachineId;
                }

                string? rawId = null;

                // 1. Primary: Windows OS Cryptography MachineGuid (unique per Windows installation)
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    rawId = key?.GetValue("MachineGuid") as string;
                }
                catch
                {
                    // Fallback if registry read is restricted
                }

                // 2. Secondary fallback: Machine name combined with System User
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    rawId = $"{Environment.MachineName}_{Environment.UserName}";
                }

                // Hash with a salt to create an anonymous 64-character hex ID
                using var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawId.Trim().ToLowerInvariant() + "_PocketMC_DeviceId_Salt"));
                _cachedMachineId = Convert.ToHexString(hash).ToLowerInvariant();

                return _cachedMachineId;
            }
        }
    }
}
