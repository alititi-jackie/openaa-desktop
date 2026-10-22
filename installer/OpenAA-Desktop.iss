; OpenAA Desktop - Inno Setup script
; Per-user install (no admin), Start menu shortcut, optional desktop shortcut,
; standard uninstall entry in Windows Settings > Apps.

#define MyAppName "OpenAA Desktop"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "OpenAA"
#define MyAppExeName "OpenAA-Desktop.exe"

[Setup]
AppId={{7E3B0B5E-1D4A-4F6E-9C2A-5B8D3E7F0A12}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\OpenAA Desktop
DefaultGroupName=OpenAA Desktop
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=OpenAA-Desktop-Setup
OutputDir=..\dist
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\OpenAA Desktop"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 OpenAA Desktop"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OpenAA Desktop"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; If the user enabled autostart in the app, the Run value lives here;
; remove it on uninstall so nothing is left behind.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OpenAADesktop"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 OpenAA Desktop"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Make sure the app is not running while files are removed. }
    Exec('taskkill.exe', '/IM OpenAA-Desktop.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
