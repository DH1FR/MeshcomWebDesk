# Node verbinden

WebDesk und der MeshCom-Node tauschen Nachrichten über das **EXTUDP-Protokoll** aus: JSON-Pakete über **UDP, Port 1799**.
Beide Geräte müssen dafür im **selben Netzwerk** (LAN oder WLAN) erreichbar sein.

```
MeshCom-Node  ◄──── UDP 1799 ────►  WebDesk-Rechner
192.168.1.60                         192.168.1.100
(Beispiel-IP)                        (Beispiel-IP)
```

> 📋 Alle IP-Adressen, Ports und Rufzeichen in diesem Handbuch sind **Beispiele**. In deinem Netz sehen die Adressen
> anders aus, z. B. `10.0.0.x` oder `172.16.x.x`.

## Der schnelle Weg: Einrichtungs-Assistent

Wenn noch kein Node eingerichtet ist, fragt WebDesk beim Start, ob der **Einrichtungs-Assistent** gestartet werden soll.
Er führt Schritt für Schritt durch die Einrichtung und sucht den Node auf Wunsch im Netzwerk. Den Assistenten findest du
auch jederzeit unter **Einstellungen → Verbindung → Nodes** an jeder Node-Karte (Knopf 🧙 *Assistent*).

Der Assistent ändert nichts automatisch und sendet nichts an den Node.

## Von Hand einrichten

### Schritt 1 – Am Node: EXTUDP einschalten

| Einstellung am Node | Wert |
|---|---|
| **EXTUDP** | ein |
| **UDP-Ziel-IP** | IP-Adresse des WebDesk-Rechners (z. B. `192.168.1.100`) |
| **UDP-Ziel-Port** | `1799` |

Die IP des Rechners findest du unter Windows mit `ipconfig`, unter Linux und macOS mit `ip a` oder `ifconfig`.
Die genauen Menüs hängen von der Firmware-Version ab; unterstützt wird die MeshCom-Firmware ab **v4.35**.

> ⚠️ **Firmware ab 4.40a.10:** Als Ziel-IP nimmt der Node **keine Broadcast- oder Multicast-Adresse** mehr an
> (`255.255.255.255`, Broadcast des eigenen Netzes, `224.x.x.x`, `0.0.0.0` oder die Node-IP selbst). Trage die feste IP
> des WebDesk-Rechners ein – am besten über eine feste Adresse oder eine DHCP-Reservierung im Router.

### Schritt 2 – In WebDesk: Verbindung eintragen

Öffne **Einstellungen → Verbindung → Nodes**. Oben stehen die gemeinsamen Werte für den **Empfang (UDP)**, darunter eine
**Karte pro Node**.

| Feld | Wo | Wert |
|---|---|---|
| **Listen IP** | Empfang (UDP) | `0.0.0.0` (alle Netzwerkkarten) |
| **Listen Port** | Empfang (UDP) | `1799` |
| **Device IP** | Node-Karte | IP-Adresse des Nodes (z. B. `192.168.1.60`) |
| **Device Port** | Node-Karte | `1799` |
| **Rufzeichen** | Node-Karte | dein Rufzeichen **mit SSID**, z. B. `OE1XYZ-2` |

Listen Port und Device Port müssen zu den EXTUDP-Einstellungen am Node passen (Standard `1799`). Speichere danach die
Einstellungen.

### Schritt 3 – Firewall öffnen

Auf dem WebDesk-Rechner muss **UDP-Port 1799** eingehend erlaubt sein.

Windows (einmalig, als Administrator):

```powershell
netsh advfirewall firewall add rule name="MeshCom WebDesk UDP 1799" protocol=UDP dir=in localport=1799 action=allow
```

Linux mit ufw:

```bash
sudo ufw allow 1799/udp
```

## Prüfen, ob es funktioniert

Unten in der **Statusleiste** zeigt ein Indikator den Zustand der UDP-Verbindung:

| Anzeige | Bedeutung |
|---|---|
| 🔴 **Kein Socket** | WebDesk konnte den Port nicht öffnen (z. B. belegt oder falsche Listen IP) |
| 🟡 **Warte auf Signal** | Der Port ist offen, es ist aber noch kein Paket vom Node angekommen |
| 🟢 **Empfang OK** | Es kommen Pakete vom Node an |

Ein Klick auf den Indikator öffnet die Verbindungsprüfung. Jede Node-Karte hat außerdem den Knopf
**🔍 Einstellungen prüfen**: Er vergleicht deine Eingaben (auch ungespeicherte) und sagt, was nicht passt, zum Beispiel
das Format von IP-Adressen und Ports oder ob die **Listen IP** zu diesem Rechner gehört.

**Typische Ursachen, wenn nichts ankommt:**

- Die Ziel-IP am Node stimmt nicht oder ist eine Broadcast-Adresse (siehe oben)
- Die Firewall blockiert UDP 1799
- Die **SSID** im Rufzeichen stimmt nicht mit dem Node überein. Dann kommen Pakete an, der Status bleibt aber gelb, und
  in der Statusleiste erscheint **⚠️ Rufzeichen?**. Ein Klick darauf öffnet die Prüfung.
- Node und Rechner sind nicht im selben Netz

## Mehrere Nodes

WebDesk kann **mehrere Nodes gleichzeitig** verwenden. Jeder Node hat eine eigene Karte mit Name, Rufzeichen, IP und
Port, eigenen Chat-Tabs und eigenem Monitor. Genau ein Node ist der **Primär-Node**: Nur er steuert MH-Liste, Karte, Baken,
Telemetrie und Bot sowie OTA und Neustart des Nodes. Neue Nodes legst du am Ende des Abschnitts an, am einfachsten mit
**+ Node mit Assistent hinzufügen**.

## Node ohne WLAN: Bluetooth-Hub

Nodes ohne WLAN (z. B. Heltec T114) lassen sich über den
[MeshCom UDP Bluetooth Hub](https://github.com/DH1FR/MeshComUDPBluetoothHub) anbinden. Der Hub gibt sich WebDesk
gegenüber als Node mit EXTUDP aus. Läuft er auf demselben Rechner, belegen Hub und WebDesk nicht denselben Port:

| WebDesk-Einstellung | Wert |
|---|---|
| Device IP | `127.0.0.1` (oder IP des Hub-Rechners) |
| Device Port | `1798` (der „UDP port“ des Hubs) |
| Listen IP / Listen Port | `127.0.0.1` oder `0.0.0.0` / `1799` |
| Rufzeichen | das des Nodes, mit SSID |

Im Hub verbindest du den Node per Bluetooth (die Handy-App vorher trennen) und trägst bei „WebDesk“ `127.0.0.1:1799` ein.

## Zusätzlich: KISS/TCP

WebDesk spricht mit einem Node immer über EXTUDP. Zusätzlich kann jeder Node über seine **KISS/TCP-Schnittstelle**
(TCP-Port 8001) verbunden werden, wenn die Firmware das unterstützt. KISS ist eine **Ergänzung, kein Ersatz**: Beide
Verbindungen laufen nebeneinander. Du schaltest es in der Node-Karte unter **Erweitert → KISS/TCP** ein. Alles Weitere
steht in der [KISS/TCP-Anleitung](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/kiss-tcp-guide-en.md) (Englisch).

## Ausführliche Anleitung

Alle Einzelheiten, auch zur Net-Console und zum automatischen Abgleich der Node-Einstellungen, stehen in der
[Anleitung zur Node-Verbindung](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/node-connection-de.md).

Weiter mit [Die Oberfläche](03-interface.md).
