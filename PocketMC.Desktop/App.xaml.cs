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
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Composition;
using PocketMC.Desktop.Infrastructure.Power;

using System.Net.Http;
using System.Net;
using System.IO;

namespace PocketMC.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private Guid? _pendingSummaryInstanceId;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Application host has not been initialized.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppStartupOptions startupOptions = AppStartupOptions.Parse(e.Args);
        WindowsToastNotificationService.RegisterApplication();
        ProtocolRegistrationService.Register();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<AppStartupOptions>(startupOptions);
                services.AddCoreInfrastructure()
                        .AddInstanceManagement()
                        .AddTunneling()
                        .AddRemoteControl()
                        .AddMarketplace()
                        .AddPresentation();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        SingleInstanceService.ShowApplicationRequested += OnShowApplicationRequested;

        await _host.StartAsync();
        Services.GetRequiredService<WindowsCornerService>().RegisterGlobalWindowHook();
        Services.GetRequiredService<ServerSleepPreventionCoordinator>().Refresh();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        if (Services.GetService<IAppNavigationService>() is IAppNavigationService appNavigationService)
        {
            appNavigationService.Initialize(mainWindow);
        }
        MainWindow = mainWindow;

        if (startupOptions.ShouldStartMinimizedToTray)
        {
            mainWindow.ShowMinimizedToTray();
        }
        else
        {
            mainWindow.Show();
        }

        if (!string.IsNullOrEmpty(startupOptions.ActivatedUri))
        {
            HandleUriActivation(startupOptions.ActivatedUri);
        }

        if (_pendingSummaryInstanceId.HasValue)
        {
            var instanceId = _pendingSummaryInstanceId.Value;
            _pendingSummaryInstanceId = null;
            HandleSummaryNotificationClick(instanceId);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        SingleInstanceService.ShowApplicationRequested -= OnShowApplicationRequested;

        if (_host is not null)
        {
            try
            {
                var coordinator = Services.GetService<PocketMC.Desktop.Features.Shell.ShellStartupCoordinator>();
                coordinator?.Shutdown();
            }
            catch { }

            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            var coordinator = Services.GetService<PocketMC.Desktop.Features.Shell.ShellStartupCoordinator>();
            coordinator?.Shutdown();
        }
        catch { }
    }

    private void OnShowApplicationRequested(string? uri)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow != null)
            {
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }
                MainWindow.Show();
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus();
            }

            if (!string.IsNullOrEmpty(uri))
            {
                HandleUriActivation(uri);
            }
        });
    }

    private void HandleUriActivation(string uri)
    {
        try
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) && parsedUri.Scheme == "pocketmc")
            {
                if (parsedUri.Host == "associate-discord")
                {
                    var query = System.Web.HttpUtility.ParseQueryString(parsedUri.Query);
                    var userId = query["userId"];
                    var apiUrl = query["apiUrl"];
                    var apiKey = query["apiKey"];

                    if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(apiUrl))
                    {
                        var applicationState = Services.GetRequiredService<PocketMC.Desktop.Features.Shell.ApplicationState>();
                        var settingsManager = Services.GetRequiredService<PocketMC.Desktop.Features.Settings.SettingsManager>();
                        
                        applicationState.Settings.DiscordUserId = userId;
                        applicationState.Settings.DiscordApiUrl = apiUrl;
                        applicationState.Settings.DiscordApiKey = apiKey;
                        settingsManager.Save(applicationState.Settings);

                        Infrastructure.AppDialog.ShowInfo("Discord Linked", "PocketMC has been successfully linked to your Discord account!");

                        Task.Run(async () =>
                        {
                            try
                            {
                                using var client = new HttpClient();
                                if (!string.IsNullOrEmpty(apiKey))
                                {
                                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                                }
                                var jsonPayload = $"{{\"user_id\": {userId}}}";
                                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                                await client.PostAsync($"{apiUrl.TrimEnd('/')}/assign-role", content);
                            }
                            catch (Exception ex)
                            {
                                Services.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to call assign-role endpoint.");
                            }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Services.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to handle URI activation.");
        }
    }

    public void HandleSummaryNotificationClick(Guid instanceId)
    {
        if (_host == null)
        {
            _pendingSummaryInstanceId = instanceId;
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (Services.GetService<IAppNavigationService>() is IAppNavigationService navigationService &&
                    Services.GetService<InstanceRegistry>() is InstanceRegistry registry)
                {
                    var metadata = registry.GetById(instanceId);
                    if (metadata != null)
                    {
                        var settingsViewModel = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ServerSettingsViewModel>(Services, metadata);
                        settingsViewModel.InitialTabIndex = 8;
                        settingsViewModel.Summaries.AutoViewLatestOnLoad = true;
                        settingsViewModel.Summaries.Load(!string.IsNullOrWhiteSpace(Services.GetRequiredService<ApplicationState>().Settings.GetCurrentAiKey()));

                        var settingsPage = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ServerSettingsPage>(Services, settingsViewModel);
                        
                        navigationService.NavigateToDetailPage(
                            settingsPage, 
                            $"Settings: {metadata.Name}", 
                            DetailRouteKind.ServerSettings, 
                            DetailBackNavigation.Dashboard, 
                            true);
                            
                        if (MainWindow != null)
                        {
                            if (MainWindow.WindowState == WindowState.Minimized)
                            {
                                MainWindow.WindowState = WindowState.Normal;
                            }
                            MainWindow.Show();
                            MainWindow.Activate();
                            MainWindow.Focus();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Services.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to handle summary notification click.");
            }
        });
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
            Infrastructure.AppDialog.ShowError(
                "PocketMC Crash",
                $"PocketMC hit an unexpected error and wrote a crash report to:\n{crashReportPath}\n\nThe app will now close so it can restart cleanly.");
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
