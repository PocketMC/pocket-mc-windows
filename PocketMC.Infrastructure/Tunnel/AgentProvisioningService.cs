using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Tunnel
{
    public enum AgentConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public class AgentProvisioningService : IDisposable
    {
        private readonly PlayitAgentService _agentService;
        private readonly ApplicationState _appState;
        private readonly IPlayitDialogService _playitDialogService;

        public AgentConnectionState State { get; private set; }
        public event EventHandler<AgentConnectionState>? StateChanged;

        public AgentProvisioningService(
            PlayitAgentService agentService,
            ApplicationState appState,
            IPlayitDialogService playitDialogService)
        {
            _agentService = agentService;
            _appState = appState;
            _playitDialogService = playitDialogService;

            _agentService.OnStateChanged += OnAgentStateChanged;
            UpdateStateFromAgent();
        }

        private void OnAgentStateChanged(object? sender, PlayitAgentState agentState)
        {
            UpdateStateFromAgent();
        }

        private void UpdateStateFromAgent()
        {
            AgentConnectionState newState = AgentConnectionState.Disconnected;

            switch (_agentService.State)
            {
                case PlayitAgentState.Connected:
                    newState = AgentConnectionState.Connected;
                    break;
                case PlayitAgentState.Starting:
                case PlayitAgentState.ProvisioningAgent:
                    newState = AgentConnectionState.Connecting;
                    break;
                case PlayitAgentState.AwaitingSetupCode:
                case PlayitAgentState.Stopped:
                case PlayitAgentState.Error:
                case PlayitAgentState.Disconnected:
                case PlayitAgentState.ReauthRequired:
                    newState = AgentConnectionState.Disconnected;
                    break;
            }

            if (State != newState)
            {
                State = newState;
                StateChanged?.Invoke(this, State);
            }
        }

        public Task<AgentConnectionState> GetConnectionStateAsync()
        {
            // On demand check. We can just return the reactive state because it's driven by PlayitAgentService
            // which handles the startup and reachability checks automatically.
            return Task.FromResult(State);
        }

        public Task ConnectAsync()
        {
            _playitDialogService.ShowSetupWizard();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _agentService.OnStateChanged -= OnAgentStateChanged;
        }
    }
}

