using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Infrastructure.Logging;

namespace PocketMC.Desktop.Tests;

public class RollingFileLoggerProviderTests
{
    [Fact]
    public void Provider_WritesLogsToExpectedDailyFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pocketmc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using var provider = new RollingFileLoggerProvider(directory, LogLevel.Information, retainedFileCount: 5);
            var logger = provider.CreateLogger("PocketMC.Tests");

            logger.LogInformation("Structured startup event for {Subsystem}", "logging");

            string expectedPath = Path.Combine(directory, $"pocketmc-{DateTime.UtcNow:yyyyMMdd}.log");
            Assert.True(File.Exists(expectedPath));

            string contents = File.ReadAllText(expectedPath);
            Assert.Contains("Structured startup event", contents);
            Assert.Contains("[Information]", contents);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
