using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Views;
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

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<SettingsManager>();
                services.AddSingleton<ApplicationState>();
                services.AddSingleton<JobObject>();
                services.AddSingleton<DownloaderService>();
                services.AddSingleton<ServerProcessManager>();
                services.AddSingleton<ResourceMonitorService>();
                services.AddSingleton<BackupService>();
                services.AddSingleton<BackupSchedulerService>();
                services.AddSingleton<PlayitApiClient>();
                services.AddSingleton<PlayitAgentService>();
                services.AddSingleton<InstanceManager>();
                services.AddSingleton<WorldManager>();
                services.AddHttpClient<VanillaProvider>();
                services.AddHttpClient<PaperProvider>(client =>
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
                });
                services.AddTransient<TunnelService>();
                services.AddTransient<MainWindow>();
                services.AddTransient<JavaSetupPage>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<NewInstanceDialog>();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        await _host.StartAsync();

        var mainWindow = Services.GetRequiredService<MainWindow>();
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
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PocketMC",
            "logs");

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
}
