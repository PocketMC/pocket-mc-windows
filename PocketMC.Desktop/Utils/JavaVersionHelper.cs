using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Utils
{
    /// <summary>
    /// Utility for detecting and validating the system Java installation.
    /// </summary>
    public static class JavaVersionHelper
    {
        public struct JavaCheckResult
        {
            public bool IsAvailable;
            public int MajorVersion;
            public string FullVersion;
            public string Error;
        }

        /// <summary>
        /// Checks the local Java installation version.
        /// </summary>
        public static async Task<JavaCheckResult> CheckInstallationAsync(string javaPath = "java")
        {
            try
            {
                if (string.IsNullOrEmpty(javaPath)) javaPath = "java";

                var psi = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) throw new Exception("Could not start java process.");

                // java -version writes to stderr
                string output = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrEmpty(output)) 
                    output = await process.StandardOutput.ReadToEndAsync();

                return ParseVersion(output);
            }
            catch (Exception ex)
            {
                return new JavaCheckResult { IsAvailable = false, Error = ex.Message };
            }
        }

        public static int GetRequiredJavaVersion(string minecraftVersion)
        {
            // Handle semver or simple versions. Treat "26.1.1" as modern.
            string cleaned = minecraftVersion.Split('-')[0];
            if (Version.TryParse(cleaned, out var v))
            {
                if (v.Major >= 25 || (v.Major == 1 && v.Minor >= 25)) return 25;
                if (v.Major >= 21 || (v.Major == 1 && v.Minor >= 21)) return 21;
                if (v.Major >= 18 || (v.Major == 1 && v.Minor >= 18)) return 17;
                return 11;
            }
            return 25; // Modern default
        }

        public static string GetRecommendedJavaPath(string minecraftVersion, string appRootPath, string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath))
                return customPath;

            int req = GetRequiredJavaVersion(minecraftVersion);
            string jreFolder = $"java{req}";

            string internalPath = System.IO.Path.Combine(appRootPath, "runtime", jreFolder, "bin", "java.exe");
            return System.IO.File.Exists(internalPath) ? internalPath : "java";
        }

        private static JavaCheckResult ParseVersion(string output)
        {
            // Example: java version "1.8.0_202" or openjdk version "21.0.1"
            var match = Regex.Match(output, @"version ""(\d+)(\.(\d+))?\.?(\d+)?_?(\d+)?""");
            if (match.Success)
            {
                string fullVersion = match.Groups[1].Value;
                int major = int.Parse(fullVersion);

                // Java 8 is reported as 1.8.x
                if (major == 1)
                {
                    if (match.Groups[3].Success)
                        major = int.Parse(match.Groups[3].Value);
                }

                return new JavaCheckResult 
                { 
                    IsAvailable = true, 
                    MajorVersion = major, 
                    FullVersion = match.Groups[0].Value 
                };
            }

            return new JavaCheckResult { IsAvailable = false, Error = "Could not parse java version string." };
        }
    }
}
