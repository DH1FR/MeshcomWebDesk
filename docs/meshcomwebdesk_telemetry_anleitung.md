# Anleitung: Externe Telemetriewerte aus MeshComWebDesk senden

Praktische Schritt-für-Schritt-Anleitung, um das neue `"type":"tele"`-UDP-Telegramm
aus MeshComWebDesk heraus zu nutzen. Die vollständige Protokoll-Referenz (Feldnamen,
Grenzen, Fehlercodes) steht in [`ext_udp_telemetry.md`](ext_udp_telemetry.md) — diese
Anleitung beschreibt nur die praktischen Schritte bis zum ersten funktionierenden Test.

---

## 1. Voraussetzungen

- Node läuft mit der Firmware, die `handleExternTelemetry()` in `src/extudp_functions.cpp`
  enthält (die beiden gebauten `.bin`-Dateien für Heltec V3 / lora32).
- Node und der Rechner mit MeshComWebDesk befinden sich **im selben WLAN/LAN**.
- UDP-Port `1799` ist zwischen beiden nicht durch eine Firewall blockiert.

---

## 2. Einmalige Konfiguration am Node

Die extern-UDP-Schnittstelle ist standardmäßig **aus**. Sie muss einmalig über die
Konsole des Nodes (Serial, BLE, oder die entsprechenden Felder in der offiziellen
MeshCom-App) aktiviert werden:

```
--extudpip <IP-Adresse-von-MeshComWebDesk>
--extudp on
```

Beispiel, wenn der Rechner mit MeshComWebDesk die IP `192.168.1.43` hat:

```
--extudpip 192.168.1.43
--extudp on
```

**Wichtig:** `--extudp on` schlägt fehl ("Please set EXPUDP IP first"), wenn vorher
keine gültige IP mit `--extudpip` gesetzt wurde. Die IP darf außerdem nicht mit der
eigenen Node-IP identisch sein.

Zur Kontrolle, ob es aktiv ist:

```
--tel
```

Prüft zwar primär die Telemetrie-Konfiguration, aber im Log erscheint beim Start
zusätzlich `[EXT]...now listening at IP <Node-IP>, UDP port 1799`, sobald die
Schnittstelle erfolgreich hochgefahren ist.

> Diese Konfiguration ist **unabhängig** vom eigentlichen `"tele"`-Feature — sie ist
> dieselbe extudp-Einrichtung, die MeshComWebDesk vermutlich schon für den
> Nachrichtenversand (`"type":"msg"`) und den Empfang der Node-eigenen `"type":"pos"`/
> `"type":"tele"`-Ausgaben nutzt. Ist das bei dir schon eingerichtet, kannst du
> Schritt 2 überspringen.

---

## 3. Das Telegramm von MeshComWebDesk senden

Ein einfaches UDP-Paket (kein TCP, keine Verbindung nötig) an `<Node-IP>:1799` mit
folgendem JSON-Inhalt:

```json
{"type":"tele","values":"123.4,5.6,7.8,9.1","parm":"Wasserstand,Ext2,Ext3,Ext4","unit":"cm,V,°C,%"}
```

| Feld     | Pflicht | Inhalt                                                        |
|----------|---------|----------------------------------------------------------------|
| `type`   | ja      | immer `"tele"`                                                  |
| `values` | ja      | 1 bis 4 Messwerte, kommagetrennt, als Text (z.B. `"123.4,5.6"`)  |
| `parm`   | nein    | passende Kanalnamen, kommagetrennt (z.B. `"Wasserstand,Ext2"`)   |
| `unit`   | nein    | passende Einheiten, kommagetrennt (z.B. `"cm,V"`)                |

Der Node stellt dem `values`-Feld automatisch seinen aktuellen Akkustand voran und
sendet den kombinierten Report umgehend über LoRa.

### Schneller Vorab-Test ohne MeshComWebDesk (PowerShell)

Um die Schnittstelle unabhängig von der MeshComWebDesk-Implementierung durchzutesten,
z.B. direkt an deinem PC:

```powershell
$client = New-Object System.Net.Sockets.UdpClient
$json = '{"type":"tele","values":"123.4,5.6,7.8,9.1","parm":"Wasserstand,Ext2,Ext3,Ext4","unit":"cm,V,°C,%"}'
$bytes = [System.Text.Encoding]::ASCII.GetBytes($json)
$client.Send($bytes, $bytes.Length, "<Node-IP>", 1799) | Out-Null
$client.Close()
```

`<Node-IP>` durch die tatsächliche IP-Adresse des Nodes ersetzen.

### Beispiel in C# (falls MeshComWebDesk .NET nutzt)

```csharp
using System.Net.Sockets;
using System.Text;

var payload = "{\"type\":\"tele\",\"values\":\"123.4,5.6,7.8,9.1\",\"parm\":\"Wasserstand,Ext2,Ext3,Ext4\",\"unit\":\"cm,V,°C,%\"}";
var bytes = Encoding.ASCII.GetBytes(payload);

using var client = new UdpClient();
client.Send(bytes, bytes.Length, nodeIpAddress, 1799);
```

---

## 4. Prüfen, ob es angekommen ist

Am Node (serielle Konsole mitlesen):

```
[EXT] tele accepted: T:78,123.4,5.6,7.8,9.1
```

`78` ist dabei der aktuelle, automatisch ergänzte Akkustand in Prozent.

Danach sollte kurz darauf ein neues `T#`-Telemetrie-Paket über LoRa rausgehen
(sichtbar in MeshComWebDesk selbst, in aprs.fi, oder — falls konfiguriert — im
eigenen MQTT-Topic `meshcom/telemetry/<Rufzeichen>`).

Falls stattdessen eine dieser Meldungen im Log erscheint, sieh in
[`ext_udp_telemetry.md`, Abschnitt 4](ext_udp_telemetry.md#4-fehlerf%C3%A4lle-log-meldungen-am-node)
nach der genauen Ursache:

- `[EXT] tele ignored: node_values already configured for internal sensors`
- `[EXT] tele missing values`
- `[EXT] tele rejected: max 4 external values supported`
- `[EXT] tele rejected: payload too long for buffers`

---

## 5. Kurz zusammengefasst

1. Einmalig: `--extudpip <IP>` + `--extudp on` am Node setzen.
2. Aus MeshComWebDesk: UDP-Paket mit dem `"type":"tele"`-JSON an `<Node-IP>:1799` senden.
3. Node meldet `[EXT] tele accepted: ...` im Log und sendet den kombinierten
   Telemetriebericht (Akku + deine Werte) umgehend über LoRa.
4. Maximal 4 externe Werte pro Nachricht; funktioniert nur, solange der Node keine
   eigene Sensor-Telemetrie (`--values`) konfiguriert hat.
