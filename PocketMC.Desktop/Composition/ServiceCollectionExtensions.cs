using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Composition
{
    public static class ServiceCollectionExtensions
    {
        public static void SetDefaultUserAgent(HttpClient client)
        {
            client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0");
        }

        public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services)
        {
            services.AddSingleton<Action<Exception>>(provider => ex =>
            {
                provider.GetRequiredService<ILogger<App>>().LogError(ex, "AsyncCommand failed");
            });

            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<IAppDispatcher, WpfAppDispatcher>();
            services.AddSingleton<IFileSystem, PhysicalFileSystem>();
            services.AddSingleton<IAppNavigationService, AppNavigationService>();
            services.AddSingleton<SettingsManager>();
            services.AddSingleton<ApplicationState>();
            services.AddSingleton<JobObject>();
            services.AddSingleton<WindowsToastNotificationService>();
            services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<WindowsToastNotificationService>());
            return services;
        }

        public static IServiceCollection AddInstanceManagement(this IServiceCollection services)
        {
            services.AddHttpClient("PocketMC.Downloads", client =>
            {
                SetDefaultUserAgent(client);
                client.Timeout = TimeSpan.FromMinutes(20);
            });

            services.AddSingleton<DownloaderService>();
            services.AddSingleton<JavaAdoptiumClient>();
            services.AddSingleton<JavaRuntimeValidator>();
            services.AddSingleton<JavaProvisioningService>();
            
            services.AddSingleton<ServerProcessManager>();
            services.AddSingleton<IServerLifecycleService, ServerLifecycleService>();
            services.AddSingleton<ServerLaunchConfigurator>();
            
            services.AddSingleton<ResourceMonitorService>();
            services.AddSingleton<BackupService>();
            services.AddSingleton<BackupSchedulerService>();
            
            services.AddSingleton<InstancePathService>();
            services.AddSingleton<InstanceRegistry>();
            services.AddSingleton<InstanceManager>();
            services.AddSingleton<ServerConfigurationService>();
            services.AddSingleton<WorldManager>();

            services.AddHttpClient<VanillaProvider>(SetDefaultUserAgent);
            services.AddHttpClient<FabricProvider>(SetDefaultUserAgent);
            services.AddHttpClient<ForgeProvider>(SetDefaultUserAgent);
            services.AddHttpClient<PaperProvider>(SetDefaultUserAgent);

            return services;
        }

        public static IServiceCollection AddTunneling(this IServiceCollection services)
        {
            services.AddSingleton<PlayitAgentProcessManager>();
            services.AddSingleton<PlayitAgentStateMachine>();
            services.AddSingleton<PlayitApiClient>();
            services.AddSingleton<PlayitAgentService>();
            services.AddSingleton<InstanceTunnelOrchestrator>();
            services.AddTransient<TunnelService>();
            return services;
        }

        public static IServiceCollection AddMarketplace(this IServiceCollection services)
        {
            services.AddSingleton<ModpackParser>();
            services.AddSingleton<ModpackService>();

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
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate
            });

            return services;
        }

        public static IServiceCollection AddPresentation(this IServiceCollection services)
        {
            services.AddSingleton<IShellUIStateService, ShellUIStateService>();
            services.AddSingleton<IShellVisualService, ShellVisualService>();
            services.AddSingleton<ShellStartupCoordinator>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<TrayIconViewModel>();

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
            
            services.AddTransient<DashboardPage>();
            services.AddTransient<NewInstancePage>();
            services.AddTransient<PluginBrowserPage>();
            services.AddTransient<ServerSettingsPage>();
            services.AddTransient<ServerConsolePage>();
            return services;
        }
    }
}
