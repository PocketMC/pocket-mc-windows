using System;
using System.IO;

namespace PocketMC.E2E.Tests.Infrastructure
{
    public class TestDirectoryContext : IDisposable
    {
        public string TempPath { get; }

        public TestDirectoryContext()
        {
            TempPath = Path.Combine(Path.GetTempPath(), "pocketmc_e2e_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempPath);
        }

        public string CreateFile(string relativePath, string content = "")
        {
            var fullPath = Path.Combine(TempPath, relativePath);
            var parentDir = Path.GetDirectoryName(fullPath);
            if (parentDir != null)
            {
                Directory.CreateDirectory(parentDir);
            }
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(TempPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(TempPath))
                {
                    Directory.Delete(TempPath, true);
                }
            }
            catch
            {
                // Suppress disposal errors in test cleanup to avoid breaking test suites
            }
        }
    }
}
