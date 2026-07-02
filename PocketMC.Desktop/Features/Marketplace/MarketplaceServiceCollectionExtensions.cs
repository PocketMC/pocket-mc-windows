using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Application.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Application.Instances.Backups;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Application.Instances.Providers;
using PocketMC.Application.Instances.Updates;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Players;
using PocketMC.Desktop.Features.Players.Services;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Infrastructure.Power;
using PocketMC.Infrastructure.Http;


namespace PocketMC.Desktop.Features.Marketplace
{
    public static class MarketplaceServiceCollectionExtensions
    {
        public static IServiceCollection AddMarketplace(this IServiceCollection services)
        {
            services.AddSingleton<ModpackParser>();
            services.AddSingleton<ModpackService>();

            services.AddHttpClient<ModrinthService>(client => client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0"))
            .AddStandardResilience()
            .AddHttpMessageHandler<PocketMC.Infrastructure.Http.LoggingHttpMessageHandler>();
            services.AddHttpClient<CurseForgeService>(client =>
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            })
            .AddStandardResilience()
            .AddHttpMessageHandler<PocketMC.Infrastructure.Http.LoggingHttpMessageHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

            services.AddSingleton<PocketMC.Desktop.Features.Marketplace.Models.IAddonProvider>(
                provider => provider.GetRequiredService<ModrinthService>());
            services.AddSingleton<PocketMC.Desktop.Features.Marketplace.Models.IAddonProvider>(
                provider => provider.GetRequiredService<CurseForgeService>());
            services.AddSingleton<AddonManifestService>();
            services.AddSingleton<MarketplaceFileInstaller>();
            services.AddSingleton<AddonUpdateService>();
            services.AddSingleton<DependencyResolverService>();
            services.AddSingleton<AddonStateStore>();
            services.AddSingleton<AddonInventoryService>();
            services.AddSingleton<AddonToggleService>();
            services.AddSingleton<AddonUpdateCheckService>();
            services.AddSingleton<PocketMC.Desktop.Features.Settings.AddonAutoUpdateService>();

            services.AddSingleton<AddonMigrationPlanner>();
            services.AddSingleton<AddonMigrationStager>();
            services.AddSingleton<AddonMigrationApplier>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceUpdatePlanner>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceVersionTargetService>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceArtifactStager>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceRollbackService>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceUpdateJournalStore>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceUpdateLockService>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceUpdateApplier>();
            services.AddSingleton<PocketMC.Application.Instances.Updates.InstanceUpdateService>();

            return services;
        }
    }
}


