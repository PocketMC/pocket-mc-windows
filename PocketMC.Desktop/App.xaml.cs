using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Composition;
using PocketMC.Desktop.Infrastructure.Logging;

using System.IO;

namespace PocketMC.Desktop;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Application host has not been initialized.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WindowsToastNotificationService.RegisterApplication();
        string appLogDirectory = GetAppLogDirectory();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
#if DEBUG
                logging.AddDebug();
#endif
                logging.AddProvider(new RollingFileLoggerProvider(
                    appLogDirectory,
                    Microsoft.Extensions.Logging.LogLevel.Information,
                    retainedFileCount: 14));
            })
            .ConfigureServices(services =>
            {
                services.AddCoreInfrastructure()
                        .AddInstanceManagement()
                        .AddTunneling()
                        .AddMarketplace()
                        .AddPresentation();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        await _host.StartAsync();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        if (Services.GetService<IAppNavigationService>() is IAppNavigationService appNavigationService)
        {
            appNavigationService.Initialize(mainWindow);
        }
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception, "UI thread", showDialog: true);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            HandleUnhandledException(exception, "background thread", showDialog: true);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception, "unobserved task", showDialog: false);
        e.SetObserved();
    }

    private void HandleUnhandledException(Exception exception, string source, bool showDialog)
    {
        try
        {
            Services.GetRequiredService<ILogger<App>>()
                .LogError(exception, "Unhandled exception on {Source}.", source);
        }
        catch
        {
            // Logging should never block crash reporting.
        }

        string crashReportPath = WriteCrashReport(exception, source);
        if (!showDialog)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                $"PocketMC hit an unexpected error and wrote a crash report to:\n{crashReportPath}\n\nThe app will now close so it can restart cleanly.",
                "PocketMC Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If WPF is not in a state to show UI, the crash report is still written.
        }
    }

    private static string WriteCrashReport(Exception exception, string source)
    {
        string logDirectory = GetAppLogDirectory();
        Directory.CreateDirectory(logDirectory);

        string crashReportPath = Path.Combine(
            logDirectory,
            $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

        string contents =
            $"Timestamp (UTC): {DateTime.UtcNow:O}{Environment.NewLine}" +
            $"Source: {source}{Environment.NewLine}" +
            $"OS: {Environment.OSVersion}{Environment.NewLine}" +
            $".NET: {Environment.Version}{Environment.NewLine}" +
            $"{Environment.NewLine}{exception}";

        File.WriteAllText(crashReportPath, contents);
        return crashReportPath;
    }

    private static string GetAppLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PocketMC",
            "logs");
    }
}
