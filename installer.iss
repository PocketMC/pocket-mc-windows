[Setup]
AppName=PocketMC
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
AppVersion={#AppVersion}
DefaultDirName={autopf}\PocketMC Desktop
DefaultGroupName=PocketMC
OutputDir=ReleaseOutput
OutputBaseFilename=PocketMC_Setup
Compression=lzma2/ultra64
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
Filename: "{tmp}\windowsdesktop-runtime-win-x64.exe"; Parameters: "/install /quiet /norestart"; Check: NeedsDotNet8Desktop; StatusMsg: "Installing .NET 8 Desktop Runtime..."; Flags: waituntilterminated skipifdoesntexist runascurrentuser
Filename: "{tmp}\aspnetcore-runtime-win-x64.exe"; Parameters: "/install /quiet /norestart"; Check: NeedsAspNetCore8; StatusMsg: "Installing ASP.NET Core 8 Runtime..."; Flags: waituntilterminated skipifdoesntexist runascurrentuser
Filename: "{app}\PocketMC.Desktop.exe"; Description: "{cm:LaunchProgram,PocketMC}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  RequiresDotNetDesktop: Boolean;
  RequiresAspNetCore: Boolean;

function NeedsDotNet8Desktop: Boolean;
begin
  Result := RequiresDotNetDesktop;
end;

function NeedsAspNetCore8: Boolean;
begin
  Result := RequiresAspNetCore;
end;

function IsDotNetInstalled(Path: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(Path + '\8.0.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function IsDotNet8DesktopInstalled(): Boolean;
begin
  Result := IsDotNetInstalled(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App')) or
            IsDotNetInstalled(ExpandConstant('{pf32}\dotnet\shared\Microsoft.WindowsDesktop.App'));
end;

function IsAspNetCore8Installed(): Boolean;
begin
  Result := IsDotNetInstalled(ExpandConstant('{pf64}\dotnet\shared\Microsoft.AspNetCore.App')) or
            IsDotNetInstalled(ExpandConstant('{pf32}\dotnet\shared\Microsoft.AspNetCore.App'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  RequiresDotNetDesktop := False;
  RequiresAspNetCore := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    RequiresDotNetDesktop := not IsDotNet8DesktopInstalled();
    RequiresAspNetCore := not IsAspNetCore8Installed();

    if RequiresDotNetDesktop or RequiresAspNetCore then
    begin
      DownloadPage.Clear;
      if RequiresDotNetDesktop then
        DownloadPage.Add('https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-win-x64.exe', '');
      
      if RequiresAspNetCore then
        DownloadPage.Add('https://aka.ms/dotnet/8.0/aspnetcore-runtime-win-x64.exe', 'aspnetcore-runtime-win-x64.exe', '');
      
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          if DownloadPage.AbortedByUser then
            Log('Download aborted by user.')
          else
            MsgBox('Failed to download required .NET 8 components. You might need to run the setup while connected to the internet.', mbError, MB_OK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;
