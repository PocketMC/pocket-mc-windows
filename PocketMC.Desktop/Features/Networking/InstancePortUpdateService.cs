using System;
using System.IO;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Features.Networking
{
    /// <summary>
    /// Consolidates port update logic for the Primary Server, Geyser, and Simple Voice Chat.
    /// Used by both the Interactive Port Map and the Port Conflict resolution dialog.
    /// </summary>
    public sealed class InstancePortUpdateService
    {
        private readonly ServerConfigurationService _configurationService;
        private readonly InstanceManager _instanceManager;
        private readonly InstanceTunnelOrchestrator _tunnelOrchestrator;

        public InstancePortUpdateService(
            ServerConfigurationService configurationService,
            InstanceManager instanceManager,
            InstanceTunnelOrchestrator tunnelOrchestrator)
        {
            _configurationService = configurationService;
            _instanceManager = instanceManager;
            _tunnelOrchestrator = tunnelOrchestrator;
        }

        /// <summary>
        /// Updates the appropriate port configuration for a given PocketMC instance based on the PortBindingRole.
        /// </summary>
        public async Task UpdatePortAsync(InstanceMetadata server, string serverDir, PortBindingRole role, int newPort)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(serverDir);

            if (role == PortBindingRole.GeyserBedrock)
            {
                server.GeyserBedrockPort = newPort;
                _instanceManager.SaveMetadata(server, serverDir);
            }
            else if (role == PortBindingRole.SimpleVoiceChat)
            {
                // Delegates the port update, config patching, and tunnel auto-creation to the centralized orchestration flow
                await _tunnelOrchestrator.UpdateSimpleVoiceChatPortAsync(server, serverDir, newPort);
            }
            else
            {
                // Fallback / default: treat as primary server port
                var cfg = _configurationService.Load(server, serverDir);
                cfg.ServerPort = newPort.ToString();
                _configurationService.Save(server, serverDir, cfg);

                // Keep the metadata in sync
                server.ServerPort = newPort;
                _instanceManager.SaveMetadata(server, serverDir);
            }
        }

        /// <summary>
        /// Helper for the Interactive Port Map, which uses string-based port types ("Main", "Geyser", "Voice").
        /// </summary>
        public async Task UpdatePortFromMapTypeAsync(InstanceMetadata server, string serverDir, string portType, int newPort)
        {
            PortBindingRole role = portType switch
            {
                "Geyser" => PortBindingRole.GeyserBedrock,
                "Voice" => PortBindingRole.SimpleVoiceChat,
                _ => PortBindingRole.PrimaryServer
            };

            await UpdatePortAsync(server, serverDir, role, newPort);
        }
    }
}

