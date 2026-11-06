#ifndef KronoVersion
  #define KronoVersion "0.1.0"
#endif

[Setup]
AppId=KronoAgent
AppName=Krono Agent
AppVersion={#KronoVersion}
AppPublisher=Alon Tsur
DefaultDirName={localappdata}\Programs\Krono Agent
DefaultGroupName=Krono Agent
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
SetupIconFile=..\native\Krono.Agent\Assets\AppIcon.ico
UninstallDisplayIcon={app}\KronoAgent.exe
OutputDir=..\release
OutputBaseFilename=Krono-Agent-Setup-x64
Compression=lzma2/fast
WizardStyle=modern dynamic hidebevels
WizardImageFile=..\release\Krono-Setup-Art.bmp
WizardImageFileDynamicDark=..\release\Krono-Setup-Art.bmp
WizardImageBackColor=#10151D
WizardImageBackColorDynamicDark=#10151D
WizardSmallImageFile=
CloseApplications=yes

[Messages]
WelcomeLabel1=Install Krono Agent
WelcomeLabel2=Choose where to install Krono Agent and where to place shortcuts. Setup installs the app for your Windows account.
SelectTasksLabel2=Choose where to create Krono Agent shortcuts. You can leave both unchecked.

[Tasks]
Name: "startmenuicon"; Description: "Create a Start menu shortcut"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\release\Krono-Agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Krono Agent\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{userdesktop}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KronoAgent.exe"; Description: "Launch Krono Agent"; Flags: nowait postinstall skipifsilent

[Code]
function ShortcutSummary: String;
begin
  Result := '';
  if WizardIsTaskSelected('startmenuicon') then
    Result := 'Start menu';
  if WizardIsTaskSelected('desktopicon') then
  begin
    if Result <> '' then Result := Result + ', ';
    Result := Result + 'Desktop';
  end;
  if Result = '' then Result := 'None';
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + 'Shortcuts to create:' +
    NewLine + Space + ShortcutSummary;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.NextButton.Left := WizardForm.CancelButton.Left;
end;
