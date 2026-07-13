using Microsoft.AspNetCore.DataProtection;
using System;
using System.IO;

namespace PocketMC.Infrastructure.Security;

/// <summary>
/// Cross-platform data protector using Microsoft.AspNetCore.DataProtection.
/// On Linux, key material is written to ~/.local/share/pocketmc/keys with chmod-600 permissions.
/// On Windows, the framework uses DPAPI automatically.
/// </summary>
public sealed class CrossPlatformDataProtector
{
    private const string DefaultAppName = "pocketmc";

    private readonly IDataProtector _protector;

    /// <summary>
    /// Creates a protector using the default key directory (~/.local/share/pocketmc/keys on Linux).
    /// </summary>
    public CrossPlatformDataProtector(string purposeString)
        : this(purposeString, GetDefaultKeyDirectory())
    {
    }

    /// <summary>
    /// Creates a protector with an explicit <paramref name="keyDirectory"/>.
    /// Useful for tests that need isolated key directories.
    /// </summary>
    public CrossPlatformDataProtector(string purposeString, string keyDirectory)
    {
        Directory.CreateDirectory(keyDirectory);

        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory), builder =>
        {
            builder.SetApplicationName(DefaultAppName);
        });

        _protector = provider.CreateProtector(purposeString);

        // After the provider has written key material, enforce strict Unix permissions.
        ApplyStrictPermissionsIfUnix(keyDirectory);
    }

    /// <summary>Encrypts <paramref name="plainText"/>. Returns null/empty unchanged.</summary>
    public string? Protect(string? plainText)
    {
        if (plainText is null) return null;
        if (plainText.Length == 0) return string.Empty;
        return _protector.Protect(plainText);
    }

    /// <summary>Decrypts <paramref name="cipherText"/>. Returns null/empty unchanged.</summary>
    public string? Unprotect(string? cipherText)
    {
        if (cipherText is null) return null;
        if (cipherText.Length == 0) return string.Empty;
        return _protector.Unprotect(cipherText);
    }

    /// <summary>
    /// Returns the default key directory:
    /// - Linux:   $XDG_DATA_HOME/pocketmc/keys  (fallback: ~/.local/share/pocketmc/keys)
    /// - Windows: %LOCALAPPDATA%\pocketmc\keys
    /// </summary>
    public static string GetDefaultKeyDirectory()
    {
        if (OperatingSystem.IsLinux())
        {
            string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(xdgDataHome, DefaultAppName, "keys");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DefaultAppName,
            "keys");
    }

    /// <summary>
    /// Applies chmod-600 equivalent (UserRead | UserWrite) to every file inside <paramref name="directory"/>
    /// on Unix platforms. Also sets the directory itself to 700. This is a no-op on Windows.
    /// </summary>
    private static void ApplyStrictPermissionsIfUnix(string directory)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        const UnixFileMode keyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const UnixFileMode dirMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        try { File.SetUnixFileMode(directory, dirMode); } catch { /* best-effort */ }

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            try { File.SetUnixFileMode(file, keyFileMode); }
            catch { /* best-effort */ }
        }
    }
}
