using System;
using System.IO;

namespace PocketMC.Domain.Security
{
    /// <summary>
    /// Shared path-safety utilities for validating file paths against
    /// directory traversal attacks (zip-slip, untrusted JSON paths, etc.).
    /// </summary>
    public static class PathSafety
    {
        private static readonly char[] DirectorySeparators =
        [
            '\\',
            '/'
        ];

        /// <summary>
        /// Checks if a relative file path contains traversal sequences (../, ..\)
        /// that could escape a root directory. Safe to call during parse-time
        /// before the actual destination root is known.
        /// </summary>
        public static bool ContainsTraversal(string relativePath)
        {
            if (IsUnsafeRelativePath(relativePath))
            {
                return true;
            }

            try
            {
                _ = Path.GetFullPath(relativePath);
                return false;
            }
            catch
            {
                return true; // If Path.GetFullPath throws, the path is suspicious
            }
        }

        /// <summary>
        /// Validates that a resolved destination path remains within the given root directory.
        /// Returns the sanitized full path, or null if the path escapes the root.
        /// </summary>
        public static string? ValidateContainedPath(string rootDirectory, string relativePath)
        {
            if (IsUnsafeRelativePath(relativePath))
            {
                return null;
            }

            try
            {
                string root = EnsureTrailingDirectorySeparator(Path.GetFullPath(rootDirectory));
                string portable = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                string resolved = Path.GetFullPath(Path.Combine(rootDirectory, portable));
                return resolved.StartsWith(root, GetPathComparison()) && !HasReparsePoint(resolved) ? resolved : null;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return null;
            }
        }

        public static bool HasReparsePoint(string path)
        {
            for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return false;
        }

        public static void RequireNoLinks(string path)
        {
            if (HasReparsePoint(path)) throw new InvalidDataException("Symbolic links and reparse points are not allowed in managed file operations.");
        }

        private static bool IsUnsafeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return true;
            }

            if (Path.IsPathRooted(relativePath) || IsWindowsRootedPath(relativePath))
            {
                return true;
            }

            foreach (string segment in relativePath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "." || segment == "..")
                {
                    return true;
                }

                string stem = segment.Split('.')[0];
                if (segment.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)) ||
                    segment.EndsWith(' ') || segment.EndsWith('.') ||
                    stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                    stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                    stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                    stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWindowsRootedPath(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal))
            {
                return true;
            }

            return path.Length >= 3 &&
                   IsAsciiLetter(path[0]) &&
                   path[1] == ':' &&
                   (path[2] == '\\' || path[2] == '/');
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.EndsWith('\\') || path.EndsWith('/')
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
            return OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
