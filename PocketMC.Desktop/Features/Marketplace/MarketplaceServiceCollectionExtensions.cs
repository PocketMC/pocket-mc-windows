using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Features.InstanceCreation;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Backups;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Infrastructure.Instances.Providers;

using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Mods;
using PocketMC.Infrastructure.Mods;
using PocketMC.Infrastructure.Networking;
using PocketMC.Desktop.Features.Players;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Application.Services.Players;
using PocketMC.Infrastructure.Players;
using PocketMC.RemoteControl.Hosting;
using PocketMC.RemoteControl.Services;
using PocketMC.RemoteControl.Tunnels;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Setup;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.OS;
using PocketMC.Infrastructure.Power;
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

            services.AddSingleton<IAddonProvider>(
                provider => provider.GetRequiredService<ModrinthService>());
            services.AddSingleton<IAddonProvider>(
                provider => provider.GetRequiredService<CurseForgeService>());
            services.AddSingleton<AddonManifestService>();
            services.AddSingleton<MarketplaceFileInstaller>();
            services.AddSingleton<AddonUpdateService>();
            services.AddSingleton<DependencyResolverService>();
            services.AddSingleton<AddonStateStore>();
            services.AddSingleton<AddonInventoryService>();
            services.AddSingleton<AddonToggleService>();
            services.AddSingleton<AddonUpdateCheckService>();
            services.AddSingleton<AddonAutoUpdateService>();

            services.AddSingleton<AddonMigrationPlanner>();
            services.AddSingleton<AddonMigrationStager>();
            services.AddSingleton<AddonMigrationApplier>();
            services.AddSingleton<InstanceUpdatePlanner>();
            services.AddSingleton<InstanceVersionTargetService>();
            services.AddSingleton<InstanceArtifactStager>();
            services.AddSingleton<InstanceRollbackService>();
            services.AddSingleton<InstanceUpdateJournalStore>();
            services.AddSingleton<InstanceUpdateLockService>();
            services.AddSingleton<InstanceUpdateApplier>();
            services.AddSingleton<InstanceUpdateService>();

            return services;
        }
    }
}


