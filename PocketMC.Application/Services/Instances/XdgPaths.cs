using System;
using System.IO;

namespace PocketMC.Application.Services.Instances;

/// <summary>
/// Provides XDG Base Directory Specification-compliant path resolution on Linux,
/// and standard AppData paths on Windows.
/// </summary>
public static class XdgPaths
{
    /// <summary>
    /// Returns the config directory for <paramref name="appName"/>.
    /// On Linux: $XDG_CONFIG_HOME/{appName}  (fallback: ~/.config/{appName}).
    /// On Windows: %LOCALAPPDATA%\{appName}.
    /// </summary>
    public static string GetConfigBasePath(string appName)
    {
        if (OperatingSystem.IsLinux())
        {
            string xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(xdgConfigHome, appName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);
    }

    /// <summary>
    /// Returns the data directory for <paramref name="appName"/>.
    /// On Linux: $XDG_DATA_HOME/{appName}  (fallback: ~/.local/share/{appName}).
    /// On Windows: %LOCALAPPDATA%\{appName}.
    /// </summary>
    public static string GetDataBasePath(string appName)
    {
        if (OperatingSystem.IsLinux())
        {
            string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(xdgDataHome, appName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);
    }

    /// <summary>
    /// Atomically moves <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.
    /// On Linux/Mac (case-sensitive): performs a single <see cref="Directory.Move"/> — a rename
    /// between case-variants is a real, distinct operation and requires no temporary step.
    /// On Windows (case-insensitive): uses a 3-step intermediate-temp move for case-only renames.
    /// Throws <see cref="IOException"/> if <paramref name="destinationPath"/> already exists.
    /// </summary>
    public static void AtomicMoveDirectory(string sourcePath, string destinationPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Linux/macOS the file system is case-sensitive; foo and Foo are different directories.
            // A plain rename is already atomic at the kernel level.
            if (Directory.Exists(destinationPath))
                throw new IOException($"Destination already exists: {destinationPath}");

            Directory.Move(sourcePath, destinationPath);
            return;
        }

        // Windows case-insensitive path: if only casing differs, use a 3-step move.
        bool caseOnlyRename = string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal);

        if (caseOnlyRename)
        {
            string tempPath = destinationPath + "_" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            Directory.Move(sourcePath, tempPath);
            try
            {
                Directory.Move(tempPath, destinationPath);
            }
            catch
            {
                Directory.Move(tempPath, sourcePath);
                throw;
            }
        }
        else
        {
            if (Directory.Exists(destinationPath))
                throw new IOException($"Destination already exists: {destinationPath}");

            Directory.Move(sourcePath, destinationPath);
        }
    }
}
