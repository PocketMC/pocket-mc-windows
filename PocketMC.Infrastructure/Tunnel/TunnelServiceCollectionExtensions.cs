using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Backups;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Infrastructure.Mods;
using PocketMC.Infrastructure.Networking;
using PocketMC.Application.Services.Players;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Infrastructure;
using PocketMC.Infrastructure.Power;
using PocketMC.Infrastructure.Http;


namespace PocketMC.Infrastructure.Tunnel
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
            // Tunnel resolution is orchestrated from singleton services and keeps no
            // per-request state, so it should share the same app-wide lifetime.
            services.AddSingleton<TunnelService>();
            
            services.AddHttpClient<PocketMC.Application.Interfaces.Tunnels.IPlayitStatusService, PlayitStatusService>(client =>
                client.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop/1.0"))
            .AddStandardResilience()
            .AddHttpMessageHandler<PocketMC.Infrastructure.Http.LoggingHttpMessageHandler>();
            
            return services;
        }
    }
}

