[Setup]
AppName=PocketMC
AppVersion=1.0.0
DefaultDirName={autopf}\PocketMC Desktop
DefaultGroupName=PocketMC
OutputDir=ReleaseOutput
OutputBaseFilename=PocketMC_Setup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableDirPage=no
PrivilegesRequired=lowest
SetupIconFile=PocketMC.Desktop\icon.ico

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish_output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PocketMC"; Filename: "{app}\PocketMC.Desktop.exe"
Name: "{autodesktop}\PocketMC"; Filename: "{app}\PocketMC.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PocketMC.Desktop.exe"; Description: "{cm:LaunchProgram,PocketMC}"; Flags: nowait postinstall skipifsilent
