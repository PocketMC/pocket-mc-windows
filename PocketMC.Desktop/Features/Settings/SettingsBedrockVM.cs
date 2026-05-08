using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Settings;

public sealed class SettingsBedrockVM : ViewModelBase
{
    private readonly Action _markDirty;

    public SettingsBedrockVM(ServerSettingsProfile profile, Action markDirty)
    {
        _markDirty = markDirty;
        PermissionLevels = profile.BedrockPermissionLevels;
    }

    private string _serverPortV6 = "19133";
    public string ServerPortV6 { get => _serverPortV6; set { if (SetProperty(ref _serverPortV6, value)) _markDirty(); } }

    private bool _allowCheats;
    public bool AllowCheats { get => _allowCheats; set { if (SetProperty(ref _allowCheats, value)) _markDirty(); } }

    private bool _texturepackRequired;
    public bool TexturepackRequired { get => _texturepackRequired; set { if (SetProperty(ref _texturepackRequired, value)) _markDirty(); } }

    private bool _forceGamemode;
    public bool ForceGamemode { get => _forceGamemode; set { if (SetProperty(ref _forceGamemode, value)) _markDirty(); } }

    private string _defaultPlayerPermissionLevel = "member";
    public string DefaultPlayerPermissionLevel { get => _defaultPlayerPermissionLevel; set { if (SetProperty(ref _defaultPlayerPermissionLevel, value)) _markDirty(); } }

    private string _tickDistance = "4";
    public string TickDistance { get => _tickDistance; set { if (SetProperty(ref _tickDistance, value)) _markDirty(); } }

    public string[] PermissionLevels { get; }
}
