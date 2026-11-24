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
AppModifyPath="{app}\Krono-Repair.exe" /SILENT /SP- /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /KRONOREPAIR=1
SetupMutex=Local\KronoAgent.Setup
OutputDir=..\release
OutputBaseFilename=Krono-Agent-Setup-x64
#ifdef KronoOffline
Compression=lzma2/ultra64
SolidCompression=yes
#else
Compression=lzma2/fast
ArchiveExtraction=full
#endif
WizardStyle=modern dynamic hidebevels
WizardSizePercent=140,120
WizardImageFile=..\release\Krono-Setup-Art.bmp
WizardImageFileDynamicDark=..\release\Krono-Setup-Art.bmp
WizardImageBackColor=#10151D
WizardImageBackColorDynamicDark=#10151D
WizardSmallImageFile=
CloseApplications=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Messages]
WelcomeLabel1=Install Krono Agent
#ifdef KronoOffline
WelcomeLabel2=Choose where to install Krono Agent and where to place shortcuts. Setup includes all app files and works offline.
#else
WelcomeLabel2=Choose where to install Krono Agent and where to place shortcuts. Setup downloads the app after you click Install. An internet connection is required.
#endif
SelectTasksLabel2=Choose whether to pin Krono Agent to Start or create a desktop shortcut.

[Tasks]
Name: "startpin"; Description: "Pin Krono Agent to Start"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\release\Krono-InstalledFiles.txt"; Flags: dontcopy
#ifdef KronoOffline
Source: "..\release\Krono-Agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs sortfilesbyextension; Excludes: "*.pdb,*.map,*.test.js"
#else
Source: "{#KronoPayloadUrl}"; DestName: "Krono-Agent-Payload-x64.zip"; DestDir: "{app}"; ExternalSize: {#KronoPayloadSize}; Hash: "{#KronoPayloadHash}"; Flags: external download extractarchive ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{userprograms}\Krono Agent\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KronoAgent.exe"; Description: "Launch Krono Agent"; Flags: nowait postinstall skipifsilent
Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent; Check: IsAppUpdate

[UninstallDelete]
Type: files; Name: "{app}\Krono-Repair.exe"
Type: files; Name: "{app}\Krono-Maintenance.lock"

[Code]
var
  CleanUpgrade: Boolean;
  InstalledFiles: TStringList;
  DefaultDataDirectory, DataDirectory: String;
  MaintenanceHandle: Integer;

function CreateFile(FileName: String; Access, Share: LongWord; Security: Integer;
  Disposition, Flags: LongWord; Template: Integer): Integer;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(Handle: Integer): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function OpenProcess(Access: LongWord; Inherit: Boolean; ProcessId: LongWord): Integer;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: Integer; Timeout: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function MoveFileEx(Source, Destination: String; Flags: LongWord): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';

procedure ReleaseMaintenance;
begin
  if MaintenanceHandle > 0 then CloseHandle(MaintenanceHandle);
  MaintenanceHandle := 0;
end;

procedure DeinitializeSetup;
begin
  ReleaseMaintenance;
  if InstalledFiles <> nil then InstalledFiles.Free;
end;

function IsAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:KRONOUPDATE|0}') = '1';
end;

procedure PinStartShortcut;
var
  ErrorCode: Integer;
begin
  if not ShellExec('startpin', ExpandConstant('{userprograms}\Krono Agent\Krono Agent.lnk'),
    '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    MsgBox('To pin Krono Agent, open Start > All apps, right-click Krono Agent, and select Pin to Start.', mbInformation, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousDir, AppDir, OverrideDir, PreviousVersion: String;
  ProcessId, ProcessHandle: Integer;
begin
  Result := '';
  AppDir := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  CleanUpgrade := RegQueryStringValue(HKCU64,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\KronoAgent_is1',
    'InstallLocation', PreviousDir) and
    (CompareText(RemoveBackslashUnlessRoot(PreviousDir), AppDir) = 0);
  if ExpandConstant('{param:KRONOREPAIR|0}') = '1' then
  begin
    if not CleanUpgrade or not RegQueryStringValue(HKCU64,
       'Software\Microsoft\Windows\CurrentVersion\Uninstall\KronoAgent_is1',
       'DisplayVersion', PreviousVersion) or (PreviousVersion <> '{#KronoVersion}') then
    begin
      Result := 'This repair package does not match the installed version. Use the current Krono Agent installer.';
      Exit;
    end;
  end;
  DefaultDataDirectory := RemoveBackslashUnlessRoot(ExpandConstant('{userappdata}\krono-agent'));
  DataDirectory := DefaultDataDirectory;
  OverrideDir := GetEnv('KRONO_USER_DATA');
  if OverrideDir <> '' then
    DataDirectory := RemoveBackslashUnlessRoot(ExpandFileName(OverrideDir));
  ReleaseMaintenance;
  if not ForceDirectories(AppDir) then
  begin
    Result := 'Setup could not open the Krono Agent installation folder.';
    Exit;
  end;
  MaintenanceHandle := CreateFile(AddBackslash(AppDir) + 'Krono-Maintenance.lock', $40000000, 0, 0, 4, $80, 0);
  if MaintenanceHandle = -1 then
  begin
    Result := 'Setup could not lock the installation folder. Close any other Krono setup and retry.';
    Exit;
  end;
  ProcessId := StrToIntDef(ExpandConstant('{param:KRONOPID|0}'), 0);
  if IsAppUpdate and (ProcessId > 0) then
  begin
    ProcessHandle := OpenProcess($00100000, False, ProcessId);
    if ProcessHandle <> 0 then
    begin
      try
        if WaitForSingleObject(ProcessHandle, 30000) <> 0 then
          Result := 'Krono Agent is still closing. Close it from the notification area and retry.';
      finally
        CloseHandle(ProcessHandle);
      end;
      if Result <> '' then Exit;
    end;
  end;
  if not CleanUpgrade then Exit;
  if (Pos(Lowercase(AddBackslash(DataDirectory)), Lowercase(AddBackslash(AppDir))) = 1) or
     (Pos(Lowercase(AddBackslash(DefaultDataDirectory)), Lowercase(AddBackslash(AppDir))) = 1) then
  begin
    CleanUpgrade := False;
    Exit;
  end;
  ExtractTemporaryFile('Krono-InstalledFiles.txt');
  if InstalledFiles <> nil then InstalledFiles.Free;
  InstalledFiles := TStringList.Create;
  InstalledFiles.LoadFromFile(ExpandConstant('{tmp}\Krono-InstalledFiles.txt'));
  InstalledFiles.Sorted := True;
end;

procedure CleanObsoleteFiles(Directory, Prefix: String);
var
  Entry: TFindRec;
  Path, RelativeName: String;
begin
  if not FindFirst(AddBackslash(Directory) + '*', Entry) then Exit;
  try
    repeat
      if (Entry.Name = '.') or (Entry.Name = '..') or ((Entry.Attributes and $400) <> 0) then Continue;
      RelativeName := Prefix + Entry.Name;
      Path := AddBackslash(Directory) + Entry.Name;
      if (CompareText(Path, DataDirectory) = 0) or (CompareText(Path, DefaultDataDirectory) = 0) then Continue;
      if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        CleanObsoleteFiles(Path, RelativeName + '\');
        RemoveDir(Path);
      end
      else if (InstalledFiles.IndexOf(RelativeName) < 0) and
        not ((Prefix = '') and ((Pos('unins', Lowercase(Entry.Name)) = 1) or
          (CompareText(Entry.Name, 'Krono-Repair.exe') = 0) or
          (CompareText(Entry.Name, 'Krono-Maintenance.lock') = 0))) then
      begin
        if not DeleteFile(Path) then Log('Could not remove obsolete file: ' + Path);
      end;
    until not FindNext(Entry);
  finally
    FindClose(Entry);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir, Repair, Pending: String;
  Entry: TFindRec;
begin
  if (CurStep = ssPostInstall) and not WizardSilent and WizardIsTaskSelected('startpin') then
    PinStartShortcut;
  if CurStep <> ssPostInstall then Exit;
  AppDir := ExpandConstant('{app}');
  try
    if CleanUpgrade and FindFirst(AppDir, Entry) then
    begin
      FindClose(Entry);
      if (Entry.Attributes and $400) = 0 then CleanObsoleteFiles(AppDir, '');
    end;
    Repair := AddBackslash(AppDir) + 'Krono-Repair.exe';
    if CompareText(ExpandConstant('{srcexe}'), Repair) <> 0 then
    begin
      Pending := Repair + '.pending';
      if not FileCopy(ExpandConstant('{srcexe}'), Pending, False) or
         not MoveFileEx(Pending, Repair, 1) then
      begin
        DeleteFile(Pending);
        Log('Could not cache the Krono repair installer.');
        SuppressibleMsgBox('Krono Agent was installed, but Windows repair could not be registered. Keep this installer to repair the app later.', mbError, MB_OK, IDOK);
      end;
    end;
  finally
    ReleaseMaintenance;
  end;
end;

procedure InitializeWizard;
var
  SideArt: TBitmapImage;
  SideWidth: Integer;
begin
  SideWidth := WizardForm.WizardBitmapImage.Width;
  SideArt := TBitmapImage.Create(WizardForm);
  SideArt.Parent := WizardForm.InnerPage;
  SideArt.Bitmap.Assign(WizardForm.WizardBitmapImage.Bitmap);
  SideArt.Stretch := True;
  SideArt.SetBounds(0, 0, SideWidth, WizardForm.InnerPage.ClientHeight);
  SideArt.Anchors := [akLeft, akTop, akBottom];
  WizardForm.MainPanel.Left := SideWidth;
  WizardForm.MainPanel.Width := WizardForm.MainPanel.Width - SideWidth;
  WizardForm.InnerNotebook.Left := WizardForm.InnerNotebook.Left + SideWidth;
  WizardForm.InnerNotebook.Width := WizardForm.InnerNotebook.Width - SideWidth;
end;

function ShortcutSummary: String;
begin
  Result := 'Start menu (All apps)';
  if WizardIsTaskSelected('startpin') then
    Result := Result + ', Pin to Start';
  if WizardIsTaskSelected('desktopicon') then
    Result := Result + ', Desktop';
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
