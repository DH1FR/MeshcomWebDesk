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
