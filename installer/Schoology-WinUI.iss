#ifndef Architecture
  #define Architecture "x64"
#endif
#ifndef AppVersion
  #define AppVersion "2026.10.0-port.106"
#endif
#ifndef SourceDir
  #define SourceDir "publish\win-x64"
#endif

#define AppName "ClassWire"
#define AppExeName "ClassWire.exe"

[Setup]
AppId={{2D38A5B1-5F8D-4CB7-9F30-8F6D2A01A7C2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ClassWire
DefaultDirName={localappdata}\Programs\Schoology
DefaultGroupName=ClassWire
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-output
OutputBaseFilename=ClassWire-WinUI-{#AppVersion}-{#Architecture}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64 arm64
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=ClassWire

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a ClassWire desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ClassWire"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\ClassWire"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[InstallDelete]
; Remove previous executable and shortcut names during the in-place rebrand.
Type: files; Name: "{app}\Schoology.WinUI.*"
Type: files; Name: "{group}\Schoology WinUI.lnk"
Type: files; Name: "{autodesktop}\Schoology WinUI.lnk"

[Run]
; Normal interactive installs offer the usual post-install launch checkbox.
Filename: "{app}\{#AppExeName}"; Description: "Launch ClassWire"; Flags: postinstall nowait skipifsilent; Check: not IsUpdateRestart
; Updates pass /RESTARTAPP=1 so the updated app is relaunched after silent installation.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: IsUpdateRestart

[Code]
function IsUpdateRestart: Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;
