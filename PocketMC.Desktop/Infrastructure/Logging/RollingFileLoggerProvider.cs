using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Infrastructure.Logging;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minimumLevel;
    private readonly int _retainedFileCount;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public RollingFileLoggerProvider(string logDirectory, LogLevel minimumLevel = LogLevel.Information, int retainedFileCount = 10)
    {
        _logDirectory = logDirectory;
        _minimumLevel = minimumLevel;
        _retainedFileCount = Math.Max(1, retainedFileCount);

        Directory.CreateDirectory(_logDirectory);
        DeleteOldLogs();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, category => new RollingFileLogger(category, _logDirectory, _minimumLevel));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    private void DeleteOldLogs()
    {
        var files = new DirectoryInfo(_logDirectory)
            .GetFiles("pocketmc-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(_retainedFileCount);

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Non-fatal: retention cleanup should never crash startup.
            }
        }
    }

    private sealed class RollingFileLogger : ILogger
    {
        private static readonly object Gate = new();

        private readonly string _categoryName;
        private readonly string _logDirectory;
        private readonly LogLevel _minimumLevel;

        public RollingFileLogger(string categoryName, string logDirectory, LogLevel minimumLevel)
        {
            _categoryName = categoryName;
            _logDirectory = logDirectory;
            _minimumLevel = minimumLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var timestamp = DateTime.UtcNow;
            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            string line = string.Create(
                CultureInfo.InvariantCulture,
                $"{timestamp:O} [{logLevel}] {_categoryName} ({eventId.Id}): {message}{Environment.NewLine}");

            if (exception is not null)
            {
                line += exception + Environment.NewLine;
            }

            string filePath = Path.Combine(_logDirectory, $"pocketmc-{timestamp:yyyyMMdd}.log");

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(filePath, line);
                }
            }
            catch
            {
                // Logging failures must never take down the app.
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
