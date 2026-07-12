using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Infrastructure.WhatsNew;
using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Infrastructure.Java;
using PocketMC.Desktop.Features.Players;
using PocketMC.Application.Services.Players;
using PocketMC.Infrastructure.Players;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Setup;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Mods;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.OS;
using PocketMC.Infrastructure.Http;
using PocketMC.Infrastructure.Power;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Marketplace;

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
            services.AddTransient<PocketMC.Infrastructure.Http.LoggingHttpMessageHandler>();

            services.AddSingleton<Action<Exception>>(provider => ex =>
            {
                provider.GetRequiredService<ILogger<App>>().LogError(ex, "AsyncCommand failed");
            });

            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<IAppDispatcher, WpfAppDispatcher>();
            services.AddSingleton<IFileSystem, PhysicalFileSystem>();
            services.AddSingleton<IAssetProvider, WpfAssetProvider>();
            services.AddSingleton<IAppNavigationService, AppNavigationService>();
            services.TryAddSingleton<AppStartupOptions>(AppStartupOptions.NormalLaunch);
            services.AddSingleton<SettingsManager>();
            services.AddSingleton<ITelemetryService, TelemetryService>();
            services.AddSingleton<WindowsStartupService>();
            services.AddSingleton<WindowsCornerService>();
            services.AddSingleton<ApplicationState>();
            services.AddSingleton<JobObject>();
            services.AddSingleton<WindowsToastNotificationService>();
            services.AddSingleton<INotificationService>(
                provider => provider.GetRequiredService<WindowsToastNotificationService>());
            services.AddSingleton<IExecutionStateApi, Kernel32ExecutionStateApi>();
            services.AddSingleton<SleepPreventionService>();
            services.AddSingleton<ServerSleepPreventionCoordinator>();
            services.AddHttpClient<PocketMC.Infrastructure.AI.Providers.GeminiProvider>(c => { c.Timeout = TimeSpan.FromMinutes(3); });
            services.AddTransient<PocketMC.Application.Interfaces.AI.ILlmProvider>(sp => sp.GetRequiredService<PocketMC.Infrastructure.AI.Providers.GeminiProvider>());
            
            services.AddHttpClient<PocketMC.Infrastructure.AI.Providers.OpenAiProvider>(c => { c.Timeout = TimeSpan.FromMinutes(3); });
            services.AddTransient<PocketMC.Application.Interfaces.AI.ILlmProvider>(sp => sp.GetRequiredService<PocketMC.Infrastructure.AI.Providers.OpenAiProvider>());
            
            services.AddHttpClient<PocketMC.Infrastructure.AI.Providers.ClaudeProvider>(c => { c.Timeout = TimeSpan.FromMinutes(3); });
            services.AddTransient<PocketMC.Application.Interfaces.AI.ILlmProvider>(sp => sp.GetRequiredService<PocketMC.Infrastructure.AI.Providers.ClaudeProvider>());
            
            services.AddHttpClient<PocketMC.Infrastructure.AI.Providers.MistralProvider>(c => { c.Timeout = TimeSpan.FromMinutes(3); });
            services.AddTransient<PocketMC.Application.Interfaces.AI.ILlmProvider>(sp => sp.GetRequiredService<PocketMC.Infrastructure.AI.Providers.MistralProvider>());
            
            services.AddHttpClient<PocketMC.Infrastructure.AI.Providers.GroqProvider>(c => { c.Timeout = TimeSpan.FromMinutes(3); });
            services.AddTransient<PocketMC.Application.Interfaces.AI.ILlmProvider>(sp => sp.GetRequiredService<PocketMC.Infrastructure.AI.Providers.GroqProvider>());
            
            services.AddHttpClient<PocketMC.Infrastructure.AI.Providers.OllamaProvider>(c => { c.Timeout = TimeSpan.FromMinutes(3); });
            services.AddTransient<PocketMC.Application.Interfaces.AI.ILlmProvider>(sp => sp.GetRequiredService<PocketMC.Infrastructure.AI.Providers.OllamaProvider>());
            
            services.AddSingleton<PocketMC.Application.Interfaces.AI.ILlmProviderFactory, PocketMC.Infrastructure.AI.LlmProviderFactory>();

services.AddSingleton<PocketMC.Desktop.Features.Intelligence.SummaryStorageService>();
            services.AddSingleton<PocketMC.Desktop.Features.Intelligence.SessionSummarizationService>();

            // Singleton so that the same download pipeline is shared across all
            // callers and cannot be started twice by accident.
            services.AddSingleton<UpdateService>();
            services.AddSingleton<IApplicationLifecycleService, ApplicationLifecycleService>();
            services.AddSingleton<WhatsNewService>();

            return services;
        }

        public static IServiceCollection AddPresentation(this IServiceCollection services)
        {
            services.AddSingleton<IShellUIStateService, ShellUIStateService>();
            services.AddSingleton<AccentColorService>();
            services.AddSingleton<IShellVisualService, ShellVisualService>();
            services.AddSingleton<ShellStartupCoordinator>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<TrayIconViewModel>();
            services.AddSingleton<InstanceTunnelOrchestrator>();
            services.AddSingleton<IPlayitDialogService, PlayitDialogService>();

            services.AddTransient<MainWindow>();
            services.AddTransient<StartupUpdateWindow>();
            services.AddTransient<JavaSetupPage>();
            services.AddTransient<TunnelPage>();
            services.AddTransient<PortsMapPage>();
            services.AddTransient<AboutPage>();
            services.AddTransient<AppSettingsPage>();
            services.AddTransient<RootDirectorySetupPage>();
            services.AddTransient<PocketMC.Desktop.Features.RemoteControl.UI.RemoteControlPage>();
            services.AddTransient<PocketMC.Desktop.Features.Setup.ViewModels.RemoteControlSettingsViewModel>();

            services.AddTransient<DashboardInstanceListViewModel>();
            services.AddTransient<DashboardMetricsViewModel>();
            services.AddTransient<DashboardActionsViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<ServerSettingsViewModel>();
            services.AddTransient<CloudBackupSettingsViewModel>();
            services.AddTransient<PocketMC.Infrastructure.Instances.InstanceImportViewModel>();
            services.AddTransient<PocketMC.Desktop.Features.Instances.ImportExport.InstanceImportPage>();

            services.AddTransient<DashboardPage>();
            services.AddTransient<NewInstancePage>();
            services.AddTransient<PluginBrowserPage>();
            services.AddTransient<ServerSettingsPage>();
            services.AddTransient<ServerConsolePage>();
            services.AddTransient<PlayerManagementPage>();
            return services;
        }
    }
}




