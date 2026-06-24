using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Tunnel
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
        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;

        public AgentConnectionState State { get; private set; }
        public event EventHandler<AgentConnectionState>? StateChanged;

        public AgentProvisioningService(
            PlayitAgentService agentService,
            ApplicationState appState,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider)
        {
            _agentService = agentService;
            _appState = appState;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;

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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<PlayitSetupWizardDialog>(_serviceProvider);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                dialog.ShowDialog();
            });

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _agentService.OnStateChanged -= OnAgentStateChanged;
        }
    }
}

