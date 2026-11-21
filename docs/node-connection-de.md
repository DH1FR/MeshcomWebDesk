# 🔌 MeshCom-Node: Verbindungseinstellungen für WebDesk

Diese Anleitung erklärt, welche Einstellungen am **MeshCom-Node** und in **WebDesk** vorgenommen werden müssen, damit die Kommunikation funktioniert.

---

## Voraussetzung: Gleiches Netzwerk

Node und WebDesk-PC müssen im **selben lokalen Netzwerk (LAN/WLAN)** erreichbar sein.

> 📋 **Hinweis zu den Beispielwerten:** Alle IP-Adressen, Ports und Rufzeichen in dieser Anleitung sind **Beispiele**. In deinem Netzwerk können die Adressen völlig anders aussehen (z. B. `10.0.0.x`, `172.16.x.x` oder ein anderer Adressbereich). Passe alle Werte entsprechend deiner eigenen Netzwerkkonfiguration an.

```
MeshCom-Node  ◄──── UDP 1799 ────►  WebDesk-PC
192.168.1.60                         192.168.1.100
(Beispiel-IP)                        (Beispiel-IP)
```

---

## Schritt 1 – Node-Firmware konfigurieren (EXTUDP)

WebDesk kommuniziert über das **EXTUDP JSON-Protokoll**. Dieses muss im Node aktiviert werden.

> ℹ️ Die genauen Menüpfade hängen von der Firmware-Version ab.  
> Kompatibel ab **MeshCom-Firmware v4.35+**

| Einstellung im Node-Menü | Wert |
|--------------------------|------|
| **EXTUDP aktivieren** | ✅ Ein |
| **UDP-Ziel-IP** | IP-Adresse des WebDesk-PCs (z. B. `192.168.1.100`) |
| **UDP-Ziel-Port** | `1799` |

> 💡 **Tipp:** Die IP des WebDesk-PCs findest du unter Windows mit `ipconfig`, unter Linux/macOS mit `ip a` oder `ifconfig`.

> ⚠️ **Firmware ab 4.40a.10:** Als UDP-Ziel-IP wird **keine Broadcast-/Multicast-Adresse** mehr akzeptiert (`255.255.255.255`, Broadcast des eigenen Subnetzes, `224.x.x.x`, `0.0.0.0`, die Node-IP selbst). Der Node meldet dann `[EXT] target … refused` und sendet nichts. Trage immer die **konkrete IP des WebDesk-PCs** ein (`--extudpip 192.168.1.100`).

---

## Schritt 2 – WebDesk Einstellungen (`/settings` → 🔌 Verbindung)

| Feld | Empfohlener Wert | Bedeutung |
|------|-----------------|-----------|
| **Listen IP** | `0.0.0.0` | Lauscht auf allen Netzwerkschnittstellen |
| **Listen Port** | `1799` | UDP-Port, auf dem WebDesk eingehende Nachrichten empfängt |
| **Device IP** | `192.168.1.60` | IP-Adresse des MeshCom-Nodes |
| **Device Port** | `1799` | UDP-Port des Nodes für ausgehende Nachrichten |
| **Eigenes Rufzeichen** | z. B. `OE1XYZ-2` | Dein Rufzeichen inkl. SSID |

> ⚠️ **Listen Port** und **Device Port** müssen mit der EXTUDP-Konfiguration des Nodes übereinstimmen (Standard: `1799`).

---

## Schritt 3 – Firewall prüfen

Stelle sicher, dass **UDP-Port 1799** auf dem WebDesk-PC **nicht blockiert** wird.

**Windows-Firewall (einmalig, als Administrator):**
```powershell
netsh advfirewall firewall add rule name="MeshCom WebDesk UDP 1799" protocol=UDP dir=in localport=1799 action=allow
```

**Linux (ufw):**
```bash
sudo ufw allow 1799/udp
```

---

## Verbindungsprüfung und Einrichtungs-Assistent

In **Einstellungen → 🔌 Verbindung** gibt es zwei Schaltflächen:

- **🔍 Einstellungen prüfen** prüft die aktuellen Werte (auch ungespeicherte) und sagt, was nicht passt und was zu ändern ist: Format von IP-Adressen und Ports, ob die **Listen IP** zu diesem PC gehört (häufiger Fehler: die IP des Nodes statt `0.0.0.0`), ob der UDP-Socket offen ist, ob der Node erreichbar ist, ob Pakete ankommen und ob **Rufzeichen und SSID** zum Node passen.
- **🧙 Einrichtungs-Assistent** führt Schritt für Schritt durch die Einrichtung (WLAN-Node, Bluetooth-Hub auf diesem PC oder auf einem anderen Rechner) Gleich im ersten Schritt wählen Sie *Node automatisch konfigurieren* (voreingestellt; WebDesk stellt ext UDP und die Ziel-IP über die Net-Console ein; ist sie ausgeschaltet, führt der Assistent über die Weboberfläche des Nodes zum Einschalten) oder *Node manuell konfigurieren* (Befehle zum Kopieren). Die Werte werden erst nach Klick auf *In Formular übernehmen* eingetragen; danach müssen Sie noch speichern.

Beide ändern **nichts automatisch** und senden **nichts an den Node**. Auch bei **Weiteren Nodes** gibt es pro Node einen Prüfen-Knopf (🔍) und einen Assistenten (🧙).

Ist der **UDP-Status in der Statusleiste rot** oder gelb, öffnet ein Klick darauf die Prüfung (rot: direkt in den Einstellungen mit dem konkreten Fehlergrund).

**Erster Start:** Ist noch kein Node eingerichtet (keine Node-Liste und nach dem Start innerhalb weniger Sekunden keine Daten von einem Node; auch wenn alle Nodes gelöscht wurden) fragt WebDesk beim Start sofort, ob der Einrichtungs-Assistent gestartet werden soll (*Ja, Assistent starten* / *Später* / *Nicht mehr fragen*; pro Browser-Tab einmal). *Später* lässt oben im Chat ein Banner mit *Einrichtung starten* als Erinnerung stehen; es öffnet die Einstellungen mit dem Assistenten. *Nicht mehr fragen* bzw. *Nicht mehr anzeigen* blendet Frage und Banner dauerhaft aus. Nach dem ersten Speichern erscheint es nicht mehr.

**Node im Netzwerk suchen:** Im Node-Schritt des Assistenten startet die Suche automatisch; die Treffer stehen in einer **Auswahlliste** am IP-Feld (*Erneut suchen* wiederholt sie). Die Suche löst das Rufzeichen als Hostname auf (die Firmware nutzt es als DHCP-/mDNS-Namen) auf, zeigt IPs, von denen schon Pakete eingegangen sind, und fragt die Adressen Ihres lokalen /24-Netzes auf einen MeshCom-Webserver (Port 80, Seitentitel „RUFZEICHEN - Meshcom“) ab. Das findet nur Nodes mit eingeschaltetem Webserver; ein reiner ext-UDP-Node meldet sich nicht von selbst. Ein Klick auf einen Treffer übernimmt nur die IP in den Assistenten.

**Einstellungen am Node lesen und korrigieren (Net-Console):** Ist die Net-Console des Nodes eingeschaltet (Port 2323, HMAC-Passwort), zeigt die Prüfung unten den Bereich *Node-Einstellungen über die Net-Console*. *Vom Node lesen* meldet ext UDP, die Ziel-IP und das Rufzeichen des Nodes. Weicht etwas ab, bietet *Am Node korrigieren …* die Befehle `--extudp on` und `--extudpip <IP dieses PCs>` an; gesendet wird erst nach Ihrer Bestätigung, und nur diese beiden Befehle. Das Rufzeichen am Node wird nie geändert; stattdessen übernimmt *Rufzeichen … in WebDesk übernehmen* den Wert des Nodes ins Formular. Ist die Net-Console nicht erreichbar, weist die Prüfung darauf hin, dass sie am Node eingeschaltet sein muss (Weboberfläche: IP Network Settings → net console, oder `--netconsole on`; Passwort mit `--passwd`).

**Automatischer Abgleich aller Node-Einstellungen:** Ist die Net-Console erreichbar und das Node-Passwort bekannt, liest die Prüfung den Node bei jedem Lauf selbst aus (nur lesend, `--info`) und vergleicht, was WebDesk dazu konfiguriert hat: **Rufzeichen/SSID**, **ext UDP** (an) und **Ziel-IP** (diese PC-IP), die **IP-Adresse des Nodes** (gegen die Device IP) sowie bei aktivem KISS **KISS/TCP, TX, RxMeta und Auth**. Zusätzlich zeigt sie, ob `--nopmother` fremde Direktnachrichten zurückhält. Der Bereich unter der Prüfung zeigt alles als Tabelle (*Am Node* / *Erwartet*). Die Korrektur bietet nur `--extudp on`, `--extudpip`, `--kiss on`, `--kiss tx on` und `--kiss meta on` an.

**KISS am Node aktivieren:** Ist unter *Verbindung* (oder bei einem weiteren Node) KISS/TCP eingeschaltet, erscheint dort der Knopf *KISS am Node über die Net-Console aktivieren*. Er liest den Node aus und bietet `--kiss on`, `--kiss tx on` und `--kiss meta on` an – gesendet wird erst nach Ihrer Bestätigung. Nur ESP32-Nodes mit passender Firmware bieten KISS.

**Rufzeichen auf beiden Seiten übernehmen:** Weicht das Rufzeichen ab, gibt es zwei Knöpfe. *Rufzeichen … in WebDesk übernehmen* trägt den Wert des Nodes ins Formular ein (danach speichern). *Rufzeichen am Node auf … ändern …* setzt es am Node auf das Rufzeichen aus WebDesk (`--setcall`, über die Net-Console). Das geschieht nur nach Klick und Bestätigung; die Warnung weist darauf hin, dass sich die Identität des Nodes im Mesh ändert und der Node neu starten kann. Es wird immer nur dieser eine Befehl gesendet.

**Falsche SSID sofort bemerken:** Auch ohne Net-Console fällt eine Abweichung auf: Sendet WebDesk eine Nachricht und der Node meldet sie unter einem anderen Rufzeichen zurück, erscheint in der Statusleiste das Warn-Badge **⚠️ Rufzeichen?** (Klick öffnet die Prüfung) und die Prüfung nennt das Rufzeichen, das der Node wirklich nutzt. WebDesk ändert nichts automatisch.

**Rufzeichen und SSID:** WebDesk erkennt eigene Pakete am Rufzeichen. Stimmt die SSID nicht mit dem Node überein, kommen zwar Pakete an, der UDP-Status bleibt aber gelb. Die Prüfung vergleicht mit dem, was der Node selbst meldet (Position, Telemetrie, Nachrichten-Echo oder `--info` über die Net-Console) und nennt den korrekten Wert.

---

## Bluetooth-Hub (MeshCom UDP Bluetooth Hub)

Nodes ohne WLAN (z. B. Heltec T114) lassen sich über den **MeshCom UDP Bluetooth Hub** anbinden. Der Hub gibt sich WebDesk gegenüber als Node mit ext-UDP aus. Läuft er auf demselben PC, belegen Hub und WebDesk nicht denselben Port:

| WebDesk-Einstellung | Wert |
|---|---|
| Device IP | `127.0.0.1` (oder IP des Hub-Rechners) |
| Device Port | `1798` (der „UDP port“ des Hubs) |
| Listen IP / Listen Port | `127.0.0.1` oder `0.0.0.0` / `1799` |
| Rufzeichen | **das des Nodes**, mit SSID |

Im Hub: Node per Bluetooth verbinden (Handy-App vorher trennen), „WebDesk“ = `127.0.0.1:1799`. Die Verbindungsprüfung erkennt den Hub-Betrieb, prüft, ob auf dem Hub-Port etwas lauscht, und warnt vor Port-Konflikten. **Zwei Nodes mit derselben IP** (z. B. zwei Hubs auf einem PC) kann WebDesk nicht unterscheiden, weil es Pakete über die Absender-IP zuordnet.

---

## Monitor-Filter

Im **Monitor-Fenster** (unterer Bereich) befindet sich in der Titelleiste ein Suchfeld 🔍.

- Eingabe eines **Rufzeichens** oder **Textfragments** filtert die Einträge sofort
- Suche in: `Von`, `An`, `Nachrichtentext`, `Rohdaten`
- **Groß-/Kleinschreibung** spielt keine Rolle, Position im Text ist egal
- Der Zähler wechselt auf `X / Y Einträge` wenn ein Filter aktiv ist
- **×**-Button löscht den Filter wieder

---

## Statusanzeige in der Statusleiste

| Symbol | Bedeutung |
|--------|-----------|
| 🔴 UDP **Kein Socket** | UDP-Port konnte nicht gebunden werden (z. B. Port bereits belegt) |
| 🟡 UDP **Warte auf Signal** | Socket aktiv, aber noch kein Paket vom Node empfangen |
| 🟢 UDP **Empfang OK** | Mindestens ein UDP-Paket wurde vom Node empfangen |

> ℹ️ Da UDP verbindungslos ist, gibt es kein klassisches „Verbunden". Grün bedeutet: Daten kommen tatsächlich an.

---

## Typische Fehlerquellen

| Symptom | Mögliche Ursache |
|---------|-----------------|
| Status: 🔴 Kein Socket | UDP-Port bereits belegt oder Berechtigungsfehler |
| Status: 🟡 Warte auf Signal | Node sendet nicht an die richtige IP / falscher Port |
| Keine eingehenden Nachrichten | Firewall blockiert UDP 1799 |
| Kann senden, aber nicht empfangen | Listen IP falsch (z. B. konkrete IP statt `0.0.0.0`) |
| Kann empfangen, aber nicht senden | Device IP oder Device Port falsch |
| Nachrichten kommen doppelt an | Node ist an zwei Ziel-IPs konfiguriert |

---

## Beispiel: Vollständige Konfiguration

```
Node-IP:          192.168.1.60
WebDesk-PC-IP:    192.168.1.100

--- Node-Firmware ---
EXTUDP:           Aktiviert
UDP-Ziel-IP:      192.168.1.100  ← WebDesk-PC
UDP-Ziel-Port:    1799

--- WebDesk appsettings / Settings-Seite ---
ListenIp:         0.0.0.0
ListenPort:       1799
DeviceIp:         192.168.1.60   ← Node
DevicePort:       1799
MyCallsign:       OE1XYZ-2
```

---

## Weiterführende Links

- 🏠 [MeshCom Projekt](https://icssw.org/meshcom/)
- 💾 [MeshCom-Firmware auf GitHub](https://github.com/icssw-org/MeshCom-Firmware)
- 🔗 [MeshCom WebDesk Repository](https://github.com/DH1FR/MeshcomWebDesk)
- 📖 [Home Assistant Telemetrie-Integration](homeassistant-telemetry.md)
