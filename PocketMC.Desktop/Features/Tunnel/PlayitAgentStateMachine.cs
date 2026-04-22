using System;

namespace PocketMC.Desktop.Features.Tunnel
{
    /// <summary>
    /// Represents the current state of the Playit.gg background agent.
    /// </summary>
    public enum PlayitAgentState
    {
        Stopped,
        AwaitingSetupCode,
        ProvisioningAgent,
        Starting,
        Connected,
        ReauthRequired,
        Error,
        Disconnected
    }

    /// <summary>
    /// Manages the state and transition events for the Playit agent.
    /// </summary>
    public sealed class PlayitAgentStateMachine
    {
        private PlayitAgentState _state = PlayitAgentState.Stopped;

        public PlayitAgentState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    OnStateChanged?.Invoke(_state);
                }
            }
        }

        public event Action<PlayitAgentState>? OnStateChanged;

        public void TransitionTo(PlayitAgentState newState)
        {
            State = newState;
        }

        public void Reset()
        {
            State = PlayitAgentState.Stopped;
        }
    }
}
