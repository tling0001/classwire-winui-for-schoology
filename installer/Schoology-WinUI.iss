#ifndef Architecture
  #define Architecture "x64"
#endif
#ifndef AppVersion
  #define AppVersion "2026.10.0-port.47"
#endif
#ifndef SourceDir
  #define SourceDir "publish\win-x64"
#endif

#define AppName "Schoology WinUI"
#define AppExeName "Schoology.WinUI.exe"

[Setup]
AppId={{2D38A5B1-5F8D-4CB7-9F30-8F6D2A01A7C2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Schoology
DefaultDirName={localappdata}\Programs\Schoology
DefaultGroupName=Schoology
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-output
OutputBaseFilename=Schoology-WinUI-{#AppVersion}-{#Architecture}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64 arm64
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=Schoology WinUI

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a Schoology WinUI desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Schoology WinUI"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Schoology WinUI"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Normal interactive installs offer the usual post-install launch checkbox.
Filename: "{app}\{#AppExeName}"; Description: "Launch Schoology WinUI"; Flags: postinstall nowait skipifsilent; Check: not IsUpdateRestart
; Updates pass /RESTARTAPP=1 so the updated app is relaunched after silent installation.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: IsUpdateRestart

[Code]
function IsUpdateRestart: Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;
