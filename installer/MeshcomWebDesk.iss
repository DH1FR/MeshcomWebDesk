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
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#AppName}
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=2024 {#AppPublisher}

; Additional wizard languages are only added when this Inno Setup installation ships them,
; so a missing file never breaks the build (the custom texts below fall back to English).
#if FileExists(CompilerPath + "Languages\Spanish.isl")
  #define HaveSpanish
#endif
#if FileExists(CompilerPath + "Languages\French.isl")
  #define HaveFrench
#endif
#if FileExists(CompilerPath + "Languages\Italian.isl")
  #define HaveItalian
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"
#ifdef HaveSpanish
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
#endif
#ifdef HaveFrench
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
#endif
#ifdef HaveItalian
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
#endif

[CustomMessages]
; Default (English) – used by every language that has no own entry
TaskStartService=Start the service after installation
GroupService=Windows service:
TaskDesktopIcon=Create a desktop shortcut
GroupIcons=Icons:
TaskAutostart=Open the browser automatically at Windows startup
GroupAutostart=Autostart:
IconOpenBrowser=Open %1 in browser
IconUninstall=Uninstall %1
StatusInstallService=Installing Windows service...
StatusStartService=Starting service...
ServiceDescription=MeshCom WebDesk – Mesh network monitor and web interface

german.TaskStartService=Dienst nach der Installation starten
german.GroupService=Windows-Dienst:
german.TaskDesktopIcon=Desktop-Verknüpfung erstellen
german.GroupIcons=Symbole:
german.TaskAutostart=Browser beim Windows-Start automatisch öffnen
german.GroupAutostart=Autostart:
german.IconOpenBrowser=%1 im Browser öffnen
german.IconUninstall=%1 deinstallieren
german.StatusInstallService=Windows-Dienst wird installiert...
german.StatusStartService=Dienst wird gestartet...
german.ServiceDescription=MeshCom WebDesk – Mesh Network Monitor und Web-Interface

#ifdef HaveSpanish
spanish.TaskStartService=Iniciar el servicio tras la instalación
spanish.GroupService=Servicio de Windows:
spanish.TaskDesktopIcon=Crear un acceso directo en el escritorio
spanish.GroupIcons=Iconos:
spanish.TaskAutostart=Abrir el navegador automáticamente al iniciar Windows
spanish.GroupAutostart=Inicio automático:
spanish.IconOpenBrowser=Abrir %1 en el navegador
spanish.IconUninstall=Desinstalar %1
spanish.StatusInstallService=Instalando el servicio de Windows...
spanish.StatusStartService=Iniciando el servicio...
spanish.ServiceDescription=MeshCom WebDesk – monitor y interfaz web de la red Mesh
#endif

#ifdef HaveFrench
french.TaskStartService=Démarrer le service après l'installation
french.GroupService=Service Windows :
french.TaskDesktopIcon=Créer un raccourci sur le Bureau
french.GroupIcons=Icônes :
french.TaskAutostart=Ouvrir automatiquement le navigateur au démarrage de Windows
french.GroupAutostart=Démarrage automatique :
french.IconOpenBrowser=Ouvrir %1 dans le navigateur
french.IconUninstall=Désinstaller %1
french.StatusInstallService=Installation du service Windows...
french.StatusStartService=Démarrage du service...
french.ServiceDescription=MeshCom WebDesk – moniteur et interface web du réseau Mesh
#endif

#ifdef HaveItalian
italian.TaskStartService=Avvia il servizio dopo l'installazione
italian.GroupService=Servizio Windows:
italian.TaskDesktopIcon=Crea un collegamento sul desktop
italian.GroupIcons=Icone:
italian.TaskAutostart=Apri automaticamente il browser all'avvio di Windows
italian.GroupAutostart=Avvio automatico:
italian.IconOpenBrowser=Apri %1 nel browser
italian.IconUninstall=Disinstalla %1
italian.StatusInstallService=Installazione del servizio Windows...
italian.StatusStartService=Avvio del servizio...
italian.ServiceDescription=MeshCom WebDesk – monitor e interfaccia web della rete Mesh
#endif

[Tasks]
Name: "startservice"; Description: "{cm:TaskStartService}"; GroupDescription: "{cm:GroupService}"
Name: "desktopicon";  Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupIcons}"
Name: "autostart";    Description: "{cm:TaskAutostart}";    GroupDescription: "{cm:GroupAutostart}"; Flags: unchecked

[Files]
; All root-level publish files (exe, static-assets manifest, native DLLs, web.config, …)
; appsettings.json and .pdb are handled separately or excluded.
Source: "publish\win-x64\*"; \
  DestDir: "{app}"; Flags: ignoreversion; \
  Excludes: "appsettings.json,appsettings.Development.json,*.pdb"
; Static web assets – Bootstrap, Leaflet, Blazor framework JS, icons, CSS, …
Source: "publish\win-x64\wwwroot\*"; \
  DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs
; Application icon for shortcuts (also embedded in the exe via ApplicationIcon)
Source: "..\MeshcomWebDesk\app.ico"; DestDir: "{app}"; Flags: ignoreversion
; appsettings.json: only install if not already present (preserve user config on upgrade)
Source: "publish\win-x64\appsettings.json"; \
  DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Dirs]
; Ensure the service (LocalSystem) can write logs, data and keys
Name: "{app}\logs"; Permissions: everyone-modify
Name: "{app}\data"; Permissions: everyone-modify
Name: "{app}\keys"; Permissions: everyone-modify

[Icons]
Name: "{group}\{cm:IconOpenBrowser,{#AppName}}";  Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5162"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:IconUninstall,{#AppName}}";        Filename: "{uninstallexe}"
; Desktop shortcut (only when task is selected)
Name: "{commondesktop}\{#AppName}";             Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5162"; IconFilename: "{app}\app.ico"; Tasks: desktopicon
; Autostart shortcut: opens browser at login (only when task is selected)
Name: "{userstartup}\{#AppName}";               Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5162"; IconFilename: "{app}\app.ico"; Tasks: autostart

[Run]
; Install Windows Service
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\{#AppExeName}"" start= auto DisplayName= ""{#AppName}"""; Flags: runhidden; StatusMsg: "{cm:StatusInstallService}"
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""{cm:ServiceDescription}"""; Flags: runhidden
; Auto-restart on failure: reset after 1 day, restart after 5s / 10s / 30s
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden
; Start now (optional task)
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; Tasks: startservice; StatusMsg: "{cm:StatusStartService}"
; Open browser after install
Filename: "{sys}\cmd.exe"; Parameters: "/c start http://localhost:5162"; Flags: runhidden postinstall skipifsilent; Description: "{cm:IconOpenBrowser,{#AppName}}"

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop   {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
// Stop and remove the existing service before upgrading so files can be replaced.
procedure CurStepChanged(CurStep: TSetupStep);
var
  RC: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}',   '', SW_HIDE, ewWaitUntilTerminated, RC);
    Sleep(2000);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, RC);
    Sleep(1000);
  end;
end;
