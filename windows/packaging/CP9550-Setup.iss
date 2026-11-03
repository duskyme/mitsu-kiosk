; CP-9550 Native — unofficial TEST installer
; Compiled by windows\packaging\build-release.ps1 (passes /DMyAppVersion=…)
; PrivilegesRequired=lowest → no interactive UAC from Setup itself.
; Driver install is a separate Admin step: Install-Drivers.ps1

#ifndef MyAppVersion
  #define MyAppVersion "0.4.3"
#endif

#define MyAppName "CP-9550 Native"
#define MyAppPublisher "CP-9550 Native contributors"
#define MyAppURL "https://github.com/duskyme/mitsu-kiosk"
#define MyAppExeName "Start-Tray.ps1"

[Setup]
AppId={{A55C9550-06D3-4A55-9D77-C9550SETUP0001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} (TEST)
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\CP9550 Native
DefaultGroupName=CP-9550 Native
DisableProgramGroupPage=yes
LicenseFile=_stage\LICENSE.txt
InfoBeforeFile=_stage\START-HERE.txt
OutputBaseFilename=CP9550-Native-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
UninstallDisplayIcon={app}\bin\Cp9550BridgeTray.exe
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductTextVersion={#MyAppVersion} TEST
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut to Start-Tray"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "logonstartup"; Description: "Start Bridge Tray when I &log on to Windows (HKCU Run)"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "_stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Optional per-user logon start — no elevation. Uninstall removes the value.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CP9550BridgeTray"; ValueData: """{app}\bin\Cp9550BridgeTray.exe"" --bridge-exe ""{app}\bin\Cp9550SpoolBridge.exe"""; Flags: uninsdeletevalue; Tasks: logonstartup

[Icons]
Name: "{group}\Start CP-9550 Bridge Tray"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Start-Tray.ps1"""; WorkingDir: "{app}"
Name: "{group}\START-HERE (read me)"; Filename: "{app}\START-HERE.txt"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CP-9550 Bridge Tray"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Start-Tray.ps1"""; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\START-HERE.txt"; Description: "Open START-HERE instructions"; Flags: postinstall shellexec skipifsilent
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Start-Tray.ps1"""; Description: "Start Bridge Tray now (no Admin)"; Flags: postinstall nowait skipifsilent unchecked

[Code]
function InitializeSetup(): Boolean;
begin
  MsgBox('UNOFFICIAL TEST BUILD' #13#10 #13#10 +
    'Not WHQL. Not Mitsubishi-signed. Test-signed drivers only.' #13#10 +
    'Setup itself does not elevate (no UAC).' #13#10 +
    'After install, run Install-Drivers.ps1 from an already-elevated Admin PowerShell.',
    mbInformation, MB_OK);
  Result := True;
end;
