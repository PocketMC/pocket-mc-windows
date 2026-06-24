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


namespace PocketMC.Desktop.Features.RemoteControl
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

