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
DisableWelcomePage=no
AllowNoIcons=yes
SetupIconFile=..\native\Krono.Agent\Assets\AppIcon.ico
UninstallDisplayIcon={app}\KronoAgent.exe
OutputDir=..\release
OutputBaseFilename=Krono-Agent-Setup-x64
Compression=lzma2/fast
WizardStyle=modern dynamic
WizardImageFile=..\release\Krono-Setup-Art.bmp
WizardImageFileDynamicDark=..\release\Krono-Setup-Art.bmp
WizardImageBackColor=#10151D
WizardImageBackColorDynamicDark=#10151D
WizardSmallImageFile=
CloseApplications=yes

[Messages]
WelcomeLabel1=Install Krono Agent
WelcomeLabel2=Choose where to install Krono Agent and where to place shortcuts. Setup installs the app for your Windows account.
SelectTasksLabel2=Choose whether to add a desktop shortcut. You can skip this step.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\release\Krono-Agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KronoAgent.exe"; Description: "Launch Krono Agent"; Flags: nowait postinstall skipifsilent

[Code]
var
  SkipShortcutButton: TNewButton;

procedure SkipShortcut(CurButton: TObject);
begin
  if WizardForm.CurPageID = wpSelectProgramGroup then
    WizardForm.NoIconsCheck.Checked := True
  else
    WizardSelectTasks('');
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure InitializeWizard;
begin
  SkipShortcutButton := TNewButton.Create(WizardForm);
  SkipShortcutButton.Parent := WizardForm;
  SkipShortcutButton.Caption := 'Skip';
  SkipShortcutButton.SetBounds(ScaleX(16), WizardForm.NextButton.Top,
    WizardForm.NextButton.Width, WizardForm.NextButton.Height);
  SkipShortcutButton.Anchors := [akLeft, akBottom];
  SkipShortcutButton.Visible := False;
  SkipShortcutButton.OnClick := @SkipShortcut;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  SkipShortcutButton.Visible := (CurPageID = wpSelectProgramGroup) or
    (CurPageID = wpSelectTasks);
end;
