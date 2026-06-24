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
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Updates;
using PocketMC.Desktop.Features.Java;
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
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Infrastructure.Power;
using PocketMC.Infrastructure.Http;


namespace PocketMC.Desktop.Features.Tunnel
{
    public static class TunnelServiceCollectionExtensions
    {
        public static IServiceCollection AddTunneling(this IServiceCollection services)
        {
            services.AddHttpClient<PlayitPartnerProvisioningClient>(client =>
                client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0"))
            .AddStandardResilience()
            .AddHttpMessageHandler<PocketMC.Infrastructure.Http.LoggingHttpMessageHandler>();
            services.AddSingleton<PlayitAgentProcessManager>();
            services.AddSingleton<PlayitAgentStateMachine>();
            services.AddSingleton<PlayitApiClient>();
            services.AddSingleton<PlayitAgentService>();
            services.AddSingleton<AgentProvisioningService>();
            services.AddSingleton<InstanceTunnelOrchestrator>();
            // Tunnel resolution is orchestrated from singleton services and keeps no
            // per-request state, so it should share the same app-wide lifetime.
            services.AddSingleton<TunnelService>();
            return services;
        }
    }
}

