using System;
using System.IO;

namespace PocketMC.Desktop.Infrastructure.Security
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

            string root = EnsureTrailingDirectorySeparator(Path.GetFullPath(rootDirectory));

            string resolved = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            return resolved.StartsWith(root, GetPathComparison()) ? resolved : null;
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

                if (segment.Contains(':', StringComparison.Ordinal))
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
