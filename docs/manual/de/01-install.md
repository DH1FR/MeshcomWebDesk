# Schnellstart und Installation

In vier Schritten von der Installation zur ersten empfangenen Nachricht:

1. WebDesk **installieren** und starten (dieses Kapitel)
2. In der Oberfläche **Rufzeichen und Node-Adresse** eintragen ([Kapitel 2](02-connect-node.md))
3. Am **Node** EXTUDP einschalten und auf den WebDesk-Rechner zeigen lassen ([Kapitel 2](02-connect-node.md))
4. Die Oberfläche kennenlernen ([Kapitel 3](03-interface.md)) und die erste Nachricht schreiben ([Kapitel 4](04-writing-messages.md))

## Was du brauchst

- Einen **MeshCom-Node** mit Firmware ab **v4.35** (EXTUDP muss am Node eingeschaltet sein)
- Einen Rechner im **selben Netzwerk** wie der Node: Windows, Linux (auch Raspberry Pi 4/5) oder macOS
- **UDP-Port 1799** darf nicht von einer Firewall blockiert werden

Die fertigen Programme findest du unter [GitHub Releases](https://github.com/DH1FR/MeshcomWebDesk/releases/latest). Du musst nichts selbst bauen.

## Windows

### Mit dem Installer (empfohlen)

Lade `MeshcomWebDesk-Setup-<Version>-win-x64.exe` herunter und starte ihn. Der Installer bringt alles mit, auch die
.NET-Laufzeit. Während der Installation kannst du wählen:

- **Windows-Dienst starten** – WebDesk läuft dann im Hintergrund und startet mit Windows
- **Desktop-Verknüpfung** anlegen
- **Browser beim Windows-Start automatisch öffnen**

Danach öffnest du WebDesk im Browser unter **http://localhost:5162**.

> ⚠️ **SmartScreen-Warnung:** Windows kann beim ersten Start „Der Computer wurde durch Windows geschützt“ anzeigen, weil
> das Programm nicht signiert ist. Klicke auf **Weitere Informationen** und dann auf **Trotzdem ausführen**.

### Mit dem ZIP-Archiv

1. Installiere die [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Entpacke `MeshcomWebDesk-<Version>-win-x64.zip` in einen Ordner, z. B. `C:\meshcom`.
3. Trage in `appsettings.json` deine **Node-IP** (`DeviceIp`) und dein **Rufzeichen** (`MyCallsign`) ein.
4. Starte `MeshcomWebDesk.exe`. Der Standardbrowser öffnet sich automatisch.

## Linux und Raspberry Pi

### Mit Docker (empfohlen)

Es gibt fertige Images für `amd64` und `arm64` (Raspberry Pi 4/5):

```bash
git clone https://github.com/DH1FR/MeshcomWebDesk.git
cd MeshcomWebDesk
docker pull ghcr.io/dh1fr/meshcomwebdesk:latest
cp deploy/appsettings.linux.json appsettings.json
nano appsettings.json          # DeviceIp und MyCallsign eintragen
docker compose up -d
```

Die Oberfläche erreichst du unter **http://\<Linux-IP\>:5162**. Der Container startet nach einem Neustart von selbst
(`restart: unless-stopped`).

> ℹ️ Der Container läuft mit `network_mode: host`, damit er die UDP-Pakete des Nodes empfängt.
> Einstellungen, die du in der Oberfläche speicherst, landen im Ordner `./data` und überstehen Updates.

### Ohne Docker (systemd)

```bash
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0
tar -xzf MeshcomWebDesk-<Version>-linux-x64.tar.gz
cd MeshcomWebDesk
nano appsettings.json          # MyCallsign, DeviceIp
sudo bash install.sh           # richtet den Dienst ein, startet beim Booten
```

Auf einem Raspberry Pi nimmst du `linux-arm64` statt `linux-x64`.

Nützliche Befehle danach:

```bash
journalctl -u meshcom-webdesk -f     # Live-Log
systemctl status meshcom-webdesk     # Status
systemctl restart meshcom-webdesk    # nach Änderungen neu starten
```

## macOS

Installiere die .NET-10-Laufzeit, entpacke das passende Archiv (`osx-arm64` für Apple Silicon, `osx-x64` für Intel),
trage in `appsettings.json` `MyCallsign` und `DeviceIp` ein und starte `MeshcomWebDesk`. Die Oberfläche erreichst du
unter **http://localhost:5162**.

## Von anderen Geräten erreichen

Standardmäßig lauscht WebDesk auf Port **5162**. Vom Handy oder Tablet im selben Netz erreichst du es unter
`http://<IP-des-Rechners>:5162`. In WebDesk zeigt der Knopf **QR** im Menü einen QR-Code, den du mit dem Handy scannst.

> 🔒 WebDesk ist für jeden erreichbar, der die Adresse kennt. Wenn andere Personen im Netz mitlesen oder senden
> könnten, richte unter **Einstellungen → System → Zugriffsschutz** ein Passwort ein.

## Aktualisieren

- **Windows, Linux, macOS ohne Docker:** Auf der Seite **Über → Aktualisieren / Sicherungen** (`/update`) aktualisiert
  WebDesk sich selbst und legt vorher eine Sicherung an.
- **Docker:** Hier wird das Image ausgetauscht, nicht die Dateien. Entweder per Klick auf der Update-Seite (mit
  `docker-compose.image.yml` und Watchtower) oder mit einem Befehl, z. B. `sudo ./scripts/update-docker.sh --image`.
  Alle Wege beschreibt die [Docker-Update-Anleitung](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/docker-update-de.md).
  Deine Daten in `./data`, `./logs` und `./keys` bleiben erhalten.

Weiter mit [Node verbinden](02-connect-node.md).
