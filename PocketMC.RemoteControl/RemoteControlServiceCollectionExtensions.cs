using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Players;

using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Infrastructure.Instances.Updates;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Mods;
using PocketMC.Infrastructure.Networking;
using PocketMC.Infrastructure.Players;
using PocketMC.RemoteControl.Hosting;
using PocketMC.RemoteControl.Services;
using PocketMC.RemoteControl.Tunnels;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;


using PocketMC.Infrastructure.Tunnel;
using PocketMC.Domain.Storage;


namespace PocketMC.RemoteControl
{
    public static class RemoteControlServiceCollectionExtensions
    {
        public static IServiceCollection AddRemoteControl(this IServiceCollection services)
        {
            services.AddSingleton<RemoteAuthenticationService>();
            services.AddSingleton<RemoteStatusService>();
            services.AddSingleton<RemoteInstanceControlService>();
            services.AddSingleton<RemotePlayerActionService>();
            services.AddSingleton<RemoteAuditLogService>();
            services.AddSingleton<LocalNetworkAddressService>();
            services.AddSingleton<RemoteRequestLimiter>();
            services.AddSingleton<RemoteConsoleWebSocketHandler>();
            services.AddSingleton<RemoteDashboardHost>();
            services.AddSingleton<RemoteControlCoordinator>();
            services.AddSingleton<ICloudflaredInstaller, CloudflaredInstaller>();
            services.AddSingleton<IRemoteTunnelProvider, CloudflaredQuickTunnelProvider>();
            services.AddSingleton<IRemoteTunnelProvider, PlayitHttpsTunnelProvider>();
            services.AddSingleton<RemoteTunnelManager>();
            services.AddHostedService<RemoteControlHostedService>();
            return services;
        }
    }
}

