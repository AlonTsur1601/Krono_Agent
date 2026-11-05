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
DisableProgramGroupPage=no
SetupIconFile=..\native\Krono.Agent\Assets\AppIcon.ico
UninstallDisplayIcon={app}\KronoAgent.exe
OutputDir=..\release
OutputBaseFilename=Krono-Agent-Setup-x64
Compression=lzma2/fast
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\release\Krono-Agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KronoAgent.exe"; Description: "Launch Krono Agent"; Flags: nowait postinstall skipifsilent
