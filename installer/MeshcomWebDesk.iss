; MeshcomWebDesk – Inno Setup Installer Script
; https://jrsoftware.org/isinfo.php
;
; Prerequisites:
;   1. Publish the app first:
;      dotnet publish MeshcomWebDesk/MeshcomWebDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\publish\win-x64
;   2. Open this file in Inno Setup Compiler (or run via build script).

#define AppName      "MeshcomWebDesk"
; AppVersion can be overridden from the command line: ISCC /DAppVersion=1.2.3 MeshcomWebDesk.iss
#ifndef AppVersion
  #define AppVersion "1.7.2"
#endif
#define AppPublisher "DH1FR"
#define AppURL       "https://github.com/DH1FR/MeshcomWebDesk"
#define AppExeName   "MeshcomWebDesk.exe"
#define ServiceName  "MeshcomWebDesk"

[Setup]
AppId={{3A6F2B9E-4C1D-4E7A-B8F0-9D2E5C3A1B7F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=MeshcomWebDesk-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=2024 {#AppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "startservice"; Description: "Dienst nach der Installation starten";          GroupDescription: "Windows-Dienst:"
Name: "desktopicon";  Description: "Desktop-Verknüpfung erstellen";                 GroupDescription: "Symbole:"
Name: "autostart";    Description: "Browser beim Windows-Start automatisch öffnen"; GroupDescription: "Autostart:"; Flags: unchecked

[Files]
Source: "publish\win-x64\{#AppExeName}";   DestDir: "{app}"; Flags: ignoreversion
Source: "publish\win-x64\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Dirs]
; Ensure the service (LocalSystem) can write logs, data and keys
Name: "{app}\logs"; Permissions: everyone-modify
Name: "{app}\data"; Permissions: everyone-modify
Name: "{app}\keys"; Permissions: everyone-modify

[Icons]
Name: "{group}\{#AppName} im Browser öffnen";  Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5162"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} deinstallieren";        Filename: "{uninstallexe}"
; Desktop shortcut (only when task is selected)
Name: "{commondesktop}\{#AppName}";             Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5162"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon
; Autostart shortcut: opens browser at login (only when task is selected)
Name: "{userstartup}\{#AppName}";               Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5162"; IconFilename: "{app}\{#AppExeName}"; Tasks: autostart

[Run]
; Install Windows Service
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\{#AppExeName}"" start= auto DisplayName= ""{#AppName}"""; Flags: runhidden; StatusMsg: "Windows-Dienst wird installiert..."
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""MeshCom WebDesk – Mesh Network Monitor und Web-Interface"""; Flags: runhidden
; Auto-restart on failure: reset after 1 day, restart after 5s / 10s / 30s
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden
; Start now (optional task)
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; Tasks: startservice; StatusMsg: "Dienst wird gestartet..."
; Open browser after install
Filename: "{sys}\cmd.exe"; Parameters: "/c start http://localhost:5162"; Flags: runhidden postinstall skipifsilent; Description: "MeshcomWebDesk im Browser öffnen"

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop   {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
// Stop and remove the existing service before upgrading so files can be replaced.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}',   '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;
