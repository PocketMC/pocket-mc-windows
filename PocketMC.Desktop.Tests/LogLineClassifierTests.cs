using PocketMC.Desktop.Features.Console;

namespace PocketMC.Desktop.Tests;

public sealed class LogLineClassifierTests
{
    [Theory]
    [InlineData("[12:00:00 ERROR]: Something failed", LogLevel.Error)]
    [InlineData("[12:00:00 WARN]: Be careful", LogLevel.Warn)]
    [InlineData("[12:00:00 INFO]: <Steve> hello", LogLevel.Chat)]
    [InlineData("> stop", LogLevel.System)]
    [InlineData("[12:00:00 INFO]: Done (4.2s)! For help, type \"help\"", LogLevel.Info)]
    public void Classify_ReturnsExpectedLevel(string line, LogLevel expected)
    {
        Assert.Equal(expected, LogLineClassifier.Classify(line, LogLevel.Info));
    }

    [Fact]
    public void Classify_KeepsStackTraceAtPreviousSeverity()
    {
        Assert.Equal(LogLevel.Error, LogLineClassifier.Classify("    at com.example.Plugin.run(Plugin.java:10)", LogLevel.Error));
        Assert.Equal(LogLevel.Warn, LogLineClassifier.Classify("Caused by: warning context", LogLevel.Warn));
    }
}


