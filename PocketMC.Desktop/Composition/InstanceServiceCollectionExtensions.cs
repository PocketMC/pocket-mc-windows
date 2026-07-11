using System;
using System.Net.Http;
using System.Collections.Generic;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Infrastructure.Mods;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Domain.Models;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Mods;
using PocketMC.Application.Services.Players;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Networking;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Backups;
using PocketMC.Infrastructure.Backups.Providers;
using PocketMC.Infrastructure.Networking;
using PocketMC.Infrastructure.Monitoring;
using PocketMC.Infrastructure.Diagnostics;
using PocketMC.Infrastructure.Http;
using PocketMC.Infrastructure.Players;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Application.Services.Instances
{
    public static class InstanceServiceCollectionExtensions
    {
        public static IServiceCollection AddInstanceManagement(this IServiceCollection services)
        {
            services.AddHttpClient("PocketMC.Downloads", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.Timeout = TimeSpan.FromMinutes(20);
            }).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();

            services.AddSingleton<DownloaderService>();
            services.AddSingleton<JavaAdoptiumClient>();
            services.AddSingleton<JavaRuntimeValidator>();
            services.AddSingleton<JavaProvisioningService>();
            services.AddSingleton<ServerProcessManager>();
            services.AddSingleton<PlayerListParser>();
            services.AddSingleton<ConsoleLogHistoryService>();
            services.AddSingleton<ServerStateFileService>();
            services.AddSingleton<BanSidecarService>();
            services.AddSingleton<WhitelistService>();
            services.AddSingleton<ServerLifecycleService>();
            services.AddSingleton<IServerLifecycleService>(provider => provider.GetRequiredService<ServerLifecycleService>());
            services.AddSingleton<ForgeInstaller>();
            services.AddSingleton<JavaLaunchConfigurator>();
            services.AddSingleton<BedrockLaunchConfigurator>();
            services.AddSingleton<PocketMineLaunchConfigurator>();
            services.AddSingleton<ServerLaunchConfigurator>();
            services.AddSingleton<PortPreflightService>();
            services.AddSingleton<PortProbeService>();
            services.AddSingleton<PortLeaseRegistry>();
            services.AddSingleton<PortRecoveryService>();
            services.AddSingleton<PortFailureMessageService>();
            services.AddSingleton<InstancePortUpdateService>();
            services.AddSingleton<IResourceMonitorService, ResourceMonitorService>();
            services.AddSingleton<BackupService>();
            services.AddSingleton<BackupSchedulerService>();
            services.AddSingleton<AddonExportService>();
            services.AddSingleton<ExportManifestBuilder>();
            services.AddSingleton<ExportFileEnumerator>();
            services.AddSingleton<ExportZipWriter>();
            services.AddSingleton<IInstanceExportService, InstanceExportService>();
            services.AddSingleton<IInstanceImportService, InstanceImportService>();
            services.AddSingleton<IServerDetectionService, ServerDetectionService>();
            services.AddSingleton<IDiscordRpcService, DiscordRpcService>();
            services.AddSingleton<CloudBackupUploadHistoryStore>();
            services.AddSingleton<CloudBackupService>();

            services.AddHttpClient("OneDrive").AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            
            services.AddHttpClient("GoogleDriveProxy", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0");
            }).AddHttpMessageHandler<LoggingHttpMessageHandler>();

            services.AddSingleton<ICloudBackupProvider, OneDriveBackupProvider>();
            services.AddSingleton<ICloudBackupProvider, DropboxBackupProvider>();
            services.AddSingleton<ICloudBackupProvider, GoogleDriveBackupProvider>();
            services.AddSingleton<InstancePathService>();
            services.AddSingleton<InstanceRegistry>();
            services.AddSingleton<InstanceManager>();
            services.AddSingleton<ServerConfigurationService>();
            services.AddSingleton<ServerRuntimeSettingApplier>();
            services.AddSingleton<WorldManager>();
            services.AddSingleton<PortDiagnosticsSnapshotBuilder>();
            services.AddSingleton<DiagnosticReportingService>();
            services.AddSingleton<DependencyHealthMonitor>();
            services.AddSingleton<IGeyserDetector, GeyserDetector>();
            services.AddSingleton<ISimpleVoiceChatDetector, SimpleVoiceChatDetector>();
            services.AddSingleton<IImageProcessingService, ImageProcessingService>();
            services.AddSingleton<PhpProvisioningService>();
            services.AddSingleton<GeyserProvisioningService>();
            services.AddSingleton<BedrockAddonInstaller>();

            services.AddHttpClient<VanillaProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            services.AddHttpClient<FabricProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            services.AddHttpClient<ForgeProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            services.AddHttpClient<NeoForgeProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            services.AddHttpClient<PaperProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            services.AddHttpClient<PocketmineProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();
            services.AddHttpClient<BedrockBdsProvider>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0")).AddStandardResilience().AddHttpMessageHandler<LoggingHttpMessageHandler>();

            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<VanillaProvider>());
            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<FabricProvider>());
            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<ForgeProvider>());
            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<NeoForgeProvider>());
            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<PaperProvider>());
            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<PocketmineProvider>());
            services.AddSingleton<IServerSoftwareProvider>(provider => provider.GetRequiredService<BedrockBdsProvider>());

            return services;
        }
    }
}
