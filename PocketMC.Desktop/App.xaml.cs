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
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Intelligence;

using System.Net.Http;
using System.Net;
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

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddHttpClient("PocketMC.Downloads", client =>
                {
                    SetDefaultUserAgent(client);
                    client.Timeout = TimeSpan.FromMinutes(20);
                });
                services.AddSingleton<IDialogService, WpfDialogService>();
                services.AddSingleton<IAppDispatcher, WpfAppDispatcher>();
                services.AddSingleton<IFileSystem, PhysicalFileSystem>();
                services.AddSingleton<IAppNavigationService, AppNavigationService>();
                services.AddSingleton<SettingsManager>();
                services.AddSingleton<ApplicationState>();
                services.AddSingleton<JobObject>();
                services.AddSingleton<DownloaderService>();
                services.AddSingleton<JavaAdoptiumClient>();
                services.AddSingleton<JavaRuntimeValidator>();
                services.AddSingleton<JavaProvisioningService>();
                services.AddSingleton<WindowsToastNotificationService>();
                services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<WindowsToastNotificationService>());
                services.AddSingleton<ServerProcessManager>();
                services.AddSingleton<IServerLifecycleService, ServerLifecycleService>();
                services.AddSingleton<ServerLaunchConfigurator>();
                services.AddSingleton<IShellUIStateService, ShellUIStateService>();
                services.AddSingleton<IShellVisualService, ShellVisualService>();
                services.AddSingleton<ResourceMonitorService>();
                services.AddSingleton<BackupService>();
                services.AddSingleton<BackupSchedulerService>();
                services.AddSingleton<ModpackParser>();
                services.AddSingleton<ModpackService>();
                services.AddSingleton<ShellStartupCoordinator>();
                services.AddSingleton<PlayitAgentProcessManager>();
                services.AddSingleton<PlayitAgentStateMachine>();
                services.AddSingleton<PlayitApiClient>();
                services.AddSingleton<PlayitAgentService>();
                services.AddSingleton<InstanceTunnelOrchestrator>();
                services.AddSingleton<InstancePathService>();
                services.AddSingleton<InstanceRegistry>();
                services.AddSingleton<InstanceManager>();
                services.AddSingleton<ServerConfigurationService>();
                services.AddSingleton<WorldManager>();
                services.AddHttpClient<AiApiClient>(client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(3);
                    SetDefaultUserAgent(client);
                });
                services.AddSingleton<SummaryStorageService>();
                services.AddSingleton<SessionSummarizationService>();
                services.AddHttpClient<VanillaProvider>(SetDefaultUserAgent);
                services.AddHttpClient<FabricProvider>(SetDefaultUserAgent);
                services.AddHttpClient<ForgeProvider>(SetDefaultUserAgent);
                services.AddHttpClient<ModrinthService>(SetDefaultUserAgent);
                services.AddHttpClient<CurseForgeService>(client =>
                {
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    client.DefaultRequestHeaders.Add(
                        "Accept", "application/json, text/plain, */*");
                    client.DefaultRequestHeaders.Add(
                        "Accept-Language", "en-US,en;q=0.5");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AutomaticDecompression =
                        System.Net.DecompressionMethods.GZip |
                        System.Net.DecompressionMethods.Deflate
                });
                services.AddHttpClient<PaperProvider>(SetDefaultUserAgent);
                services.AddTransient<TunnelService>();
                services.AddTransient<MainWindow>();
                services.AddTransient<JavaSetupPage>();
                services.AddTransient<TunnelPage>();
                services.AddTransient<AboutPage>();
                services.AddTransient<AppSettingsPage>();
                services.AddTransient<RootDirectorySetupPage>();
                services.AddTransient<DashboardInstanceListVM>();
                services.AddTransient<DashboardMetricsVM>();
                services.AddTransient<DashboardActionsVM>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ServerSettingsViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<NewInstancePage>();
                services.AddTransient<PluginBrowserPage>();
                services.AddTransient<ServerSettingsPage>();
                services.AddTransient<ServerConsolePage>();
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

    private static void SetDefaultUserAgent(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0");
    }
}
