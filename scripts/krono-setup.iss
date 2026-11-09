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

[Messages]
WelcomeLabel1=Install Krono Agent
#ifdef KronoOffline
WelcomeLabel2=Choose where to install Krono Agent and where to place shortcuts. Setup includes all app files and works offline.
#else
WelcomeLabel2=Choose where to install Krono Agent and where to place shortcuts. Setup downloads the app after you click Install. An internet connection is required.
#endif
SelectTasksLabel2=Choose where to create Krono Agent shortcuts. You can leave both unchecked.

[Tasks]
Name: "startmenuicon"; Description: "Create a Start menu shortcut"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\release\Krono-InstalledFiles.txt"; Flags: dontcopy
#ifdef KronoOffline
Source: "..\release\Krono-Agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs sortfilesbyextension; Excludes: "*.pdb,*.map,*.test.js"
#else
Source: "{#KronoPayloadUrl}"; DestName: "Krono-Agent-Payload-x64.zip"; DestDir: "{app}"; ExternalSize: {#KronoPayloadSize}; Hash: "{#KronoPayloadHash}"; Flags: external download extractarchive ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{userprograms}\Krono Agent\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{userdesktop}\Krono Agent"; Filename: "{app}\KronoAgent.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KronoAgent.exe"; Description: "Launch Krono Agent"; Flags: nowait postinstall skipifsilent

[Code]
var
  CleanUpgrade: Boolean;
  InstalledFiles: TStringList;
  DefaultDataDirectory, DataDirectory: String;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousDir, AppDir, OverrideDir: String;
begin
  Result := '';
  AppDir := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  DefaultDataDirectory := RemoveBackslashUnlessRoot(ExpandConstant('{userappdata}\krono-agent'));
  DataDirectory := DefaultDataDirectory;
  OverrideDir := GetEnv('KRONO_USER_DATA');
  if OverrideDir <> '' then
    DataDirectory := RemoveBackslashUnlessRoot(ExpandFileName(OverrideDir));
  CleanUpgrade := RegQueryStringValue(HKCU64,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\KronoAgent_is1',
    'InstallLocation', PreviousDir) and
    (CompareText(RemoveBackslashUnlessRoot(PreviousDir), AppDir) = 0);
  if not CleanUpgrade then Exit;
  if (Pos(Lowercase(AddBackslash(DataDirectory)), Lowercase(AddBackslash(AppDir))) = 1) or
     (Pos(Lowercase(AddBackslash(DefaultDataDirectory)), Lowercase(AddBackslash(AppDir))) = 1) then
  begin
    CleanUpgrade := False;
    Exit;
  end;
  ExtractTemporaryFile('Krono-InstalledFiles.txt');
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
        not ((Prefix = '') and (Pos('unins', Lowercase(Entry.Name)) = 1)) then
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
  AppDir: String;
  Entry: TFindRec;
begin
  if (CurStep <> ssPostInstall) or not CleanUpgrade then Exit;
  AppDir := ExpandConstant('{app}');
  if not FindFirst(AppDir, Entry) then Exit;
  FindClose(Entry);
  if (Entry.Attributes and $400) = 0 then CleanObsoleteFiles(AppDir, '');
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
