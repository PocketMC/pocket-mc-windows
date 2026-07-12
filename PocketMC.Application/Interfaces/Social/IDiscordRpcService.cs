using System;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces
{
    public interface IDiscordRpcService : IDisposable
    {
        /// <summary>
        /// Initialize the Discord RPC client and connect if enabled in settings.
        /// Safe to call multiple times — subsequent calls are no-ops if already connected.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Force an immediate presence update based on current server states.
        /// </summary>
        void UpdatePresence();

        /// <summary>
        /// Cleanly disconnect the RPC client and clear the user's Discord status.
        /// </summary>
        void Shutdown();
    }
}
