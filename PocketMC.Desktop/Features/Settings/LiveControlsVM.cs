using System;
using System.Threading.Tasks;
using System.Windows.Input;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Settings;

/// <summary>
/// Sub-ViewModel for runtime-only live controls (gamerules, weather).
/// Only functional when the server is online. Sends commands immediately
/// via <see cref="ServerRuntimeSettingApplier"/>.
/// </summary>
public sealed class LiveControlsVM : ViewModelBase
{
    private readonly ServerRuntimeSettingApplier _runtimeApplier;
    private readonly Guid _instanceId;
    private readonly EngineFamily _engineFamily;

    private bool _isVisible;
    private bool _doDaylightCycle = true;
    private bool _doWeatherCycle = true;
    private string _selectedWeather = "clear";
    private bool _isApplying;

    public LiveControlsVM(
        ServerRuntimeSettingApplier runtimeApplier,
        Guid instanceId,
        EngineFamily engineFamily)
    {
        _runtimeApplier = runtimeApplier;
        _instanceId = instanceId;
        _engineFamily = engineFamily;

        SetWeatherCommand = new AsyncRelayCommand(_ => ApplyWeatherAsync());
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool DoDaylightCycle
    {
        get => _doDaylightCycle;
        set
        {
            if (SetProperty(ref _doDaylightCycle, value) && IsVisible)
            {
                _ = _runtimeApplier.ApplyGameruleAsync(_instanceId, _engineFamily, "doDaylightCycle", value.ToString().ToLowerInvariant());
            }
        }
    }

    public bool DoWeatherCycle
    {
        get => _doWeatherCycle;
        set
        {
            if (SetProperty(ref _doWeatherCycle, value) && IsVisible)
            {
                _ = _runtimeApplier.ApplyGameruleAsync(_instanceId, _engineFamily, "doWeatherCycle", value.ToString().ToLowerInvariant());
            }
        }
    }

    public string SelectedWeather
    {
        get => _selectedWeather;
        set => SetProperty(ref _selectedWeather, value);
    }

    public string[] WeatherOptions { get; } = { "clear", "rain", "thunder" };

    public ICommand SetWeatherCommand { get; }

    public bool IsApplying
    {
        get => _isApplying;
        private set => SetProperty(ref _isApplying, value);
    }

    /// <summary>
    /// True when the engine supports gamerule commands (Java + BDS, not PocketMine).
    /// </summary>
    public bool SupportsGamerules => _engineFamily != EngineFamily.Pocketmine;

    /// <summary>
    /// True when the engine supports the weather command (Java + BDS, not PocketMine).
    /// </summary>
    public bool SupportsWeather => _engineFamily != EngineFamily.Pocketmine;

    private async Task ApplyWeatherAsync()
    {
        if (!IsVisible || string.IsNullOrWhiteSpace(SelectedWeather)) return;

        IsApplying = true;
        try
        {
            await _runtimeApplier.ApplyWeatherAsync(_instanceId, _engineFamily, SelectedWeather);
        }
        finally
        {
            IsApplying = false;
        }
    }
}
