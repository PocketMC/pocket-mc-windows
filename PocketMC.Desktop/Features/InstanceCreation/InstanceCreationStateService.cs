using System;

namespace PocketMC.Desktop.Features.InstanceCreation;

/// <summary>
/// Tracks app-wide instance creation state without relying on static page flags.
/// </summary>
public sealed class InstanceCreationStateService
{
    private readonly object _gate = new();
    private bool _isPageOpen;
    private bool _isCreationInProgress;

    public event Action? StateChanged;

    public bool IsPageOpen
    {
        get { lock (_gate) return _isPageOpen; }
    }

    public bool IsCreationInProgress
    {
        get { lock (_gate) return _isCreationInProgress; }
    }

    public bool ShouldBlockNavigationAway
    {
        get { lock (_gate) return _isPageOpen && _isCreationInProgress; }
    }

    public void SetPageOpen(bool isOpen) => Update(isPageOpen: isOpen, isCreationInProgress: _isCreationInProgress);

    public void SetCreationInProgress(bool isInProgress) => Update(isPageOpen: _isPageOpen, isCreationInProgress: isInProgress);

    public void Reset() => Update(isPageOpen: false, isCreationInProgress: false);

    private void Update(bool isPageOpen, bool isCreationInProgress)
    {
        bool changed;
        lock (_gate)
        {
            changed = _isPageOpen != isPageOpen || _isCreationInProgress != isCreationInProgress;
            _isPageOpen = isPageOpen;
            _isCreationInProgress = isCreationInProgress;
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }
}
