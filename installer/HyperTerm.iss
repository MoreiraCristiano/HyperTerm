#ifndef AppVersion
  #error AppVersion must be provided by the release build.
#endif
#ifndef SourceDirectory
  #error SourceDirectory must be provided by the release build.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must be provided by the release build.
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename must be provided by the release build.
#endif
#ifndef SetupIconPath
  #error SetupIconPath must be provided by the release build.
#endif

[Setup]
AppId={{18FB7305-F1DD-4DAD-9E39-C8968C7B869F}
AppName=HyperTerm
AppVersion={#AppVersion}
AppVerName=HyperTerm {#AppVersion}
AppPublisher=HyperTerm
AppPublisherURL=https://github.com/MoreiraCristiano/HyperTerm
AppSupportURL=https://github.com/MoreiraCristiano/HyperTerm/issues
AppUpdatesURL=https://github.com/MoreiraCristiano/HyperTerm/releases
DefaultDirName={userpf}\HyperTerm
DefaultGroupName=HyperTerm
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.10240
OutputDir={#OutputDirectory}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#SetupIconPath}
UninstallDisplayIcon={app}\HyperTerm.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DirExistsWarning=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=HyperTerm
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=HyperTerm installer
VersionInfoCompany=HyperTerm

[Files]
Source: "{#SourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\tools\psmux"
Type: files; Name: "{app}\licenses\psmux-LICENSE.txt"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Icons]
Name: "{group}\HyperTerm"; Filename: "{app}\HyperTerm.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\HyperTerm"; Filename: "{app}\HyperTerm.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\HyperTerm.exe"; Description: "Launch HyperTerm"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Automatic ZIP updates can add files that were not present when Setup ran.
Type: filesandordirs; Name: "{app}\*"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDirectory: String;
begin
  if (CurUninstallStep <> usPostUninstall) or UninstallSilent then
    Exit;

  DataDirectory := ExpandConstant('{localappdata}\HyperTerm');
  if SuppressibleMsgBox(
      'Remove saved sessions, settings, update files, and local logs from:' + #13#10 +
      DataDirectory + '?' + #13#10#13#10 +
      'Choose No to keep this data for a future installation.',
      mbConfirmation,
      MB_YESNO,
      IDNO) = IDYES then
  begin
    if DirExists(DataDirectory) and not DelTree(DataDirectory, True, True, True) then
      MsgBox(
        'HyperTerm was uninstalled, but some local data could not be removed.',
        mbError,
        MB_OK);
  end;
end;
