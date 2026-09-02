# KISS/TCP als Node-Transport in WebDesk — Analyse

Status: **Phase A + B + C implementiert** (RX + erweiterte Monitoransicht + TX mit
0xF0-Result + KISS-Hub) und verifiziert. Branch: `feature/kiss-mode`, noch nicht
committet.

Umsetzungsplan/Details: `~/.claude/plans/ancient-sniffing-hollerith.md`.

Was in Phase A + B umgesetzt wurde:
- `NodeProfile.Transport` (`ExtUdp`|`Kiss`) + `KissPort`, Auswahl pro Node in
  Settings inkl. Verbindungsstatus-Chip; Persistenz in `appsettings.override.json`.
- `Services/Kiss/`: `KissFraming` (SLIP-Deframer/-Framer), `Ax25Ui`
  (UI-Frame decode/encode), `AprsInfo` (Positions-/Message-Parser +
  `/B /A /N /P /H /T /R` Kommentar-Extraktion), `KissClientService`
  (`BackgroundService`, 1 TCP-Verbindung je KISS-Node, Reconnect-Backoff,
  RxMeta-Zuordnung zum *vorherigen* Frame, `0xF0`-FIFO-Korrelation).
- Einspeisung in dieselbe `ChatService`-Pipeline wie ext-udp → Monitor, MH-Liste,
  Karte, DB-Sink, Webhook, MQTT unverändert.
- Monitor: voller APRS-Kommentar, `👥` Nachbarzahl, `🛰️` `/R=`-Liste,
  Digipeater-Pfad, RSSI/SNR pro Frame, TX-Result-Badge (ok / abgelehnt+Grund).
- TX-Weiche in `MeshcomUdpService.SendMessageAsync` nach `node.Transport`
  (`IMeshcomSender`-Signatur unverändert; ext-udp-Pfad nicht angefasst).
- ext-udp-`tele`-Push bleibt UDP (läuft parallel weiter).
- **Phase C — KISS-Hub:** `Services/Kiss/KissHubService.cs` (`BackgroundService`,
  `TcpListener`). `MeshcomSettings.KissHub` (`Enabled`/`Port`=8001/`NodeId`
  null=Primär/`BindLan`), Settings-Abschnitt „KISS-Hub". `KissClientService`
  liefert Hooks `OnNodeDataFrame` (RX-Fan-out) + `OnHubTxResult` (0xF0 pro
  verursachendem Downstream-Client). Nur `type 0x00` in beide Richtungen; bounded
  Channel drop-oldest je Client. Verifiziert: 2 parallele Clients, RX-Fan-out,
  TX-Injection, per-Client-0xF0-Routing.
- **ACK-Zuordnung:** der on-air-ACK `:ack<NNN>` verwendet `NNN = msg_id & 0x3FF`
  (die `{NNN`, die die Firmware an eine DM anhängt). `HandleTxResult` stempelt das
  als `SequenceNumber` → `MarkMessageAcknowledged` trifft exakt die richtige
  gesendete Nachricht.

--- ursprüngliche Analyse ---

Referenz-Spezifikation (Firmware-Seite, verbindlicher Draht-Kontrakt):
`C:\SRC\RA\MeshComFirmware\MeshCom-Firmware\docs\kiss_tcp_protocol.md`
sowie `kiss_mode_analysis.md` und `kiss_tcp_test.md` im selben Ordner.
Die KISS/TCP-Schnittstelle ist auf ESP32-Firmware **v1.2 bereits ausgeliefert**
(v1.1 getestet 2026-08-30, Heltec V3; v1.2 = TX-Result-Frame, §3.4).

**Testknoten:** `DH1FR-2` unter `192.168.1.102:8001` läuft mit der neuen
Firmware (v1.2).

### Firmware-Update (nach v1.2) — umgesetzt

- **TX-Result `0x05`** = abgelehnt, Injektions-Rate > 8 Frames/s. `0x04` deckt jetzt
  zusätzlich „Adressfeld nicht terminiert" und „control/PID ≠ 0x03/0xF0" ab.
  → `KissTxResult.RejectedRateLimit`; WebDesk pact eigene Writes auf 125 ms Abstand
  (`WorkerConnection.PaceAsync`).
- **Optionaler HMAC-Auth-Handshake** (`--kiss auth on` + `--passwd` am Node, Default
  aus): Node sendet `NONCE: <32 hex>\r\n` im Klartext, Client antwortet
  `HMAC-SHA256(passwd, nonce_bytes)` als 64 hex + CRLF, Node `OK` / `FAIL`. Erkennung
  am ersten Byte (`0xC0` = kein Auth, `'N'` = Handshake). WebDesk: `KissAuthAsync`
  in `KissClientService`, Passwort = `NodeProfile.TelnetPassword` (wie net console),
  15 s Frist, Reconnect-Backoff bei `FAIL`.
- **Client→Mesh APRS-ACK** (F1): der Node macht aus `:<addr9>:ackNN` jetzt einen
  echten MeshCom-ACK unter dem Client-Call → der Hub reicht Client-ACK-Frames
  unverändert durch, kein WebDesk-Handling nötig.
- **`--via` im Pfad / `::text` / `{` im Text / Backpressure / `--kiss off` /
  Half-open / RxMeta / HEY-Queue** (F3–F14): reine Node-Verbesserungen, kein
  WebDesk-Handlungsbedarf (Reconnect-mit-Backoff besteht bereits).
- Zweistellige SSIDs (`-16…-99`) erscheinen jetzt als `-15` (AX.25 hat nur 4
  SSID-Bits) — weiterhin verlustbehaftet, Kenntnisnahme.

---

## 1. Anforderung

1. **Pro Node** wahlweise **KISS/TCP statt ext-udp** als Transport. Einstellung
   je Node, optional, ext-udp bleibt der Default.
2. Läuft der **Primär-Node** auf KISS, sollen die **zusätzlichen Informationen**,
   die KISS gegenüber ext-udp liefert (volle Positions-Kommentarzeile, Digipeater-
   Pfad, `/R=` Relay-Liste, `/N` Nachbarzahl, Per-Frame RSSI/SNR), im
   **Monitorfenster** angezeigt und ausgewertet werden.

---

## 2. Ist-Zustand: wie ext-udp heute funktioniert

| Aspekt | Umsetzung |
|---|---|
| Socket | **Ein** `UdpClient`, gebunden auf `ListenIp:ListenPort` (Default `0.0.0.0:1799`), in [MeshcomUdpService.cs](../MeshcomWebDesk/Services/MeshcomUdpService.cs) (`BackgroundService`, Singleton). |
| Node-Zuordnung RX | Über die **Quell-IP** des UDP-Pakets → `NodeManager.ResolveNodeByIp()` ([NodeManager.cs:183](../MeshcomWebDesk/Services/NodeManager.cs#L183)). Alle Nodes teilen sich Port 1799. |
| Registrierung | WebDesk schickt `{"type":"info","src":"<call>"}` an **jeden** konfigurierten Node, damit der Node WebDesk in seine UDP-Empfängerliste aufnimmt (`RegisterWithDeviceAsync`). |
| Datenformat RX | MeshCom-eigenes JSON, nur `type` = `msg` / `pos` / `tele`. Geparst in `ParseMessage()` → strukturierte Felder (`lat`, `long`, `alt`, `temp1`, `hw_id`, `firmware`, `batt`, `rssi`, `snr`, `msg_id`, `src_type` …). |
| Datenformat TX | `{"type":"msg","dst":...,"msg":...}` bzw. `{"type":"tele",...}` per UDP an `node.DeviceIp:node.DevicePort`. |
| Node-Profil | [NodeProfile.cs](../MeshcomWebDesk/Models/NodeProfile.cs) — `DeviceIp`, `DevicePort`, `ListenIp`, `ListenPort`, `Callsign`, `IsPrimary`, `Enabled`, TLS-Konsole-Felder. **Kein Transport-Feld.** |
| Verbindungsstatus | `ConnectionStatus` — **eine** app-weite Instanz ([ConnectionStatus.cs](../MeshcomWebDesk/Models/ConnectionStatus.cs)). UDP kennt keinen Verbindungszustand; „Node still" ≠ „Node weg". |
| Monitor | [Chat.razor:241-380](../MeshcomWebDesk/Components/Pages/Chat.razor#L241) rendert `state.Messages` (Liste `MeshcomMessage`). `ChatService.AppendToMonitor()` schreibt zusätzlich in den DB-Sink. |

**Wichtig:** `MeshcomUdpService` ist eine Monolith-Klasse (1717 Zeilen). Sie macht
neben RX/TX auch: Beacon-Scheduler, Text-Telemetrie-Scheduler, ext-udp-`tele`
Push inkl. Firmware-Capability-Probe, Auto-Reply, Bot-Command-Dispatch,
Variablen-Expansion. Alles hängt am einen `_udpClient`.

---

## 3. Was KISS/TCP ändert (laut Referenz-Doku)

| Aspekt | ext-udp | KISS/TCP |
|---|---|---|
| Transport | UDP 1799, verbindungslos, Node→Client | **TCP `<node-ip>:8001`**, verbindungsorientiert, **Client→Node** |
| Sockets | 1 geteilter UDP-Socket für alle Nodes | **1 TCP-Verbindung pro KISS-Node** |
| Node-Identität | Quell-IP | die Verbindung selbst (1 Client pro Node) |
| Registrierung | nötig (`type:info`) | **entfällt** — Client verbindet sich |
| Exklusivität | mehrere Clients möglich | **Single-Client**: solange WebDesk hängt, kein Direwolf/YAAC am selben Node und umgekehrt |
| Framing | `parsePacket()` liefert Grenzen frei | **KISS-SLIP-Deframing** über TCP-Bytestrom nötig (`0xC0` FEND, `0xDB` FESC) |
| Nutzlast | MeshCom-JSON | **AX.25-UI-Frame ohne FCS** → AX.25-Adressfeld-Decoder + APRS-Parser nötig |
| Voraussetzung Node | Firmware-Support ext-udp | `--kiss on` (+ `--kiss tx on` für Senden, `--kiss meta on` für RSSI/SNR). **Nur ESP32** — RAK/nRF52-Firmware kompiliert KISS aus. |
| Reconnect | n/a | Bei Socket-Close/RST/Read-Error ist der Node weg → Retry mit Backoff |

### 3.1 Was KISS zusätzlich liefert (§2.2 / §6 der Firmware-Doku)

- **Volle APRS-Positions-Kommentarzeile**, z. B.
  `Ralf, F34, AVSK#MeshComWebDesk/B=100/A=000827/N9/P=1016.7/H=56.0/T=23.3/R=262;2626;26269;9;`
  → `/B=` Akku %, `/A=` Höhe ft, `/N` Nachbarzahl, `/P /H /T` Druck/Feuchte/Temp,
  `/R=` Relay-Node-Liste.
- **Digipeater-Pfad** aus dem AX.25-Adressfeld (die Mesh-Relays mit gesetztem
  H-Bit).
- **Per-Frame RSSI/SNR** über den optionalen RxMeta-Frame (KISS-Port 1, Typ
  `0x10`, 3 Bytes: `snr` int8 dB, `rssi` int16 LE dBm) — mit `--kiss meta on`.
- **Standardformat**: jedes KISS/APRS-Tool kann mitlesen (WebDesk könnte KISS
  weiterreichen).

### 3.2 Was KISS **nicht** liefert (weiter ext-udp-Domäne)

- Strukturiertes `tele`-JSON (`temp1/hum/qfe/...` als Zahlen) — bei KISS nur
  `/T= /H=` roh im Kommentar.
- `hw_id` / `firmware` / `fw_sub` des Absenders (kein APRS-Feld).
- HEY (`@`), ACK-Frames, reine Telemetrie-Frames — werden **nicht** über KISS
  geschickt.
- `msg_id` (kein APRS-Feld) → `msg_id`-basierte Monitor-Features bleiben bei
  KISS leer; Echo-/ACK-Matching für eigene TX läuft über die Sequenznummer.

### 3.3 ext-udp und KISS laufen im Node gleichzeitig

Die Firmware betreibt den KISS-Server (TCP 8001) **und** den ext-udp-Listener
(UDP 1799) parallel — getrennte Sockets, die Doku (§7) nennt genau diese
Kombination als Regelfall („KISS für den Roh-Monitor, ext-udp für das
Telemetrie-Panel"). Daraus folgt das Transport-Modell von WebDesk:

- **RX-Transport ist pro Node** — ext-udp **oder** KISS (§5.1).
- **TX für Chat/Beacon/Bot** folgt dem RX-Transport des Ziel-Node (§5.4).
- **Der ext-udp-`tele`-Push** (WebDesk → Node, `{"type":"tele"}`, schreibt eigene
  Wetterstations-Werte in die Sensorvariablen des Node) läuft **immer über UDP**
  und ist vom RX-Transport unabhängig: `MeshcomUdpService` sendet weiter an
  `node.DeviceIp:1799`, auch wenn der Node per KISS empfangen wird. Die
  resultierende Bake kommt über KISS mit `/T= /H=` im Kommentar zurück; die
  Capability-Probe matcht auf die **aus dem Kommentar geparsten** Werte statt auf
  einen `tele`-Frame (den es über KISS nicht gibt). Kein Feature-Verlust.

---

## 4. Kernproblem: die Architektur passt nicht 1:1

ext-udp ist „ein Socket, viele Nodes, per IP unterschieden". KISS ist „eine
Verbindung pro Node". Der bestehende `MeshcomUdpService` kann nicht einfach
erweitert werden — er muss **transport-agnostisch** aufgeteilt werden.

```
                          ┌───────────────────────────────┐
                          │  Transport-agnostischer Kern  │
   NodeProfile.Transport  │  - RX-Dispatch: MeshcomMessage │
        ├─ ExtUdp ───────►│    → ChatService.AddXxx()      │
        └─ Kiss ─────────►│  - TX-Routing nach Node        │
                          │  - Beacon / Telemetrie / Bot / │
                          │    AutoReply (unverändert)     │
                          └──────────┬─────────┬──────────┘
                                     │         │
                   ┌─────────────────┘         └──────────────────┐
        ┌──────────▼──────────┐            ┌────────────▼─────────────┐
        │ ExtUdpTransport     │            │ KissTransport            │
        │ (heutiger UdpClient,│            │ 1 TcpClient je KISS-Node │
        │  IP-Auflösung)      │            │ KISS-Deframer + AX.25 +  │
        │                     │            │ APRS-Parser + Reconnect  │
        └─────────────────────┘            └──────────────────────────┘
```

Minimal-invasive Alternative (ohne großen Refactor): `KissClientService` als
zweiter `BackgroundService`, der **nur** RX-Frames dekodiert, daraus
`MeshcomMessage` baut und dieselben `ChatService.AddIncomingMessage /
AddPositionBeacon / AddAck`-Methoden aufruft wie der UDP-Service; TX geht über
eine gemeinsame Sender-Abstraktion. Beacon/Bot/AutoReply rufen bereits heute
`SendMessageAsync(..., sourceNodeId)` — es reicht, dort **nach Transport des
Ziel-Node zu verzweigen**. Das ist der empfohlene erste Schritt (siehe §8).

---

## 5. Vorgeschlagenes Design

### 5.1 Node-Profil erweitern

```csharp
public enum NodeTransport { ExtUdp, Kiss }

// NodeProfile
public NodeTransport Transport { get; set; } = NodeTransport.ExtUdp;
public int KissPort { get; set; } = 8001;   // fix in Firmware v1, Feld für später
```

**Kein `KissTx`- oder `KissMeta`-Schalter in WebDesk** (Entscheidung):

- **RxMeta** (RSSI/SNR pro Frame, KISS-Port 1, type `0x10`): WebDesk wertet den
  Frame immer aus, wenn er ankommt — ein Standard-KISS-Client ignoriert Port 1
  laut Spec ohnehin. Kein ankommender RxMeta → RSSI/SNR bleibt `null`. Gesteuert
  über `--kiss meta on` am Node.
- **TX**: WebDesk sendet immer; das Gate ist `--kiss tx on` am Node. Kein
  Konfig-Schalter, sondern eine **read-only Statusanzeige** „KISS TX: ok /
  abgelehnt (Grund)", gespeist aus dem TX-Result-Frame (§3.4).

### 3.4 TX-Result-Frame (Firmware KISS v1.2)

Das Protokoll wurde erweitert: der Node antwortet auf **jeden** vom Client
gesendeten `type 0x00`-Frame mit einem Result-Frame auf **KISS-Port 15
(type `0xF0`)**, Node → Client:

```
[ status : 1 ]  [ msg_id : 4  LE  — nur bei status 0x01 ]
  0x01  akzeptiert / im TX-Ring   (+ msg_id, matcht Luft & ext-udp-Echo)
  0x02  abgelehnt: Callsign passt nicht
  0x03  abgelehnt: --kiss tx off
  0x04  abgelehnt: Frame/Payload nicht verwertbar
```

Standard-KISS-Clients ignorieren Port 15. Damit ist der bisher offene stille
TX-Fehlschlag (§6.10) gelöst:

- **`0x01`** → WebDesk schreibt die `msg_id` (LE) auf die `MeshcomMessage` der
  eigenen Sendung → ACK-/Echo-Matching **exakt wie bei ext-udp**. Status „ok".
  Hinweis: `0x01` heißt **im TX-Ring**, nicht zugestellt — echte Zustellung
  weiterhin nur über MeshCom-ACK / APRS-`ack` (identisch zur ext-udp-Semantik).
- **`0x02` / `0x03` / `0x04`** → Status „abgelehnt: Callsign / tx off /
  Frame" an der Sendung + Monitor-Zeile. Kein Timeout-Raten mehr.

**Korrelation:** `0xF0` ist die Antwort auf *jeden* `0x00`-Frame; KISS ist
Single-Client und geordnet → eine **FIFO-Queue der unquittierten gesendeten
Frames** genügt (nächstes `0xF0` gehört zum ältesten Eintrag).

Persistenz: `MeshcomSettings.Nodes` wird bereits als Liste serialisiert
([SettingsService](../MeshcomWebDesk/Services/SettingsService.cs)) — nur die
neuen Properties ergänzen, Default hält Bestandskonfigs kompatibel.

### 5.2 Settings-UI ([Settings.razor](../MeshcomWebDesk/Components/Pages/Settings.razor), Node-Card ab Zeile 108)

- Radio/Select **Transport: ext-udp | KISS/TCP** pro Node-Card.
- Bei KISS: Port-Feld (Default 8001, vorerst read-only wie DevicePort). Keine
  weiteren Checkboxen (§5.1) — nur die read-only Statuszeile „KISS TX".
- Hinweistext: „Nur ESP32-Nodes. Solange WebDesk per KISS verbunden ist, kann
  kein weiteres KISS-Programm denselben Node nutzen. TX/RxMeta werden am Node
  mit `--kiss tx on` / `--kiss meta on` freigeschaltet."
- ext-udp-`tele`-Telemetrie bleibt bei KISS-Primär-Node **ohne Warnung nutzbar**
  (Push läuft weiter über UDP — §3.3).

### 5.3 Neuer `KissTransport` / `KissClientService`

Pattern-Vorlage: [TelnetService.cs](../MeshcomWebDesk/Services/TelnetService.cs)
(TCP-Client mit Reconnect/Backoff, TLS) und der `externQueue`-Deferred-Queue-
Ansatz der Firmware.

Pro KISS-Node ein Worker:
1. `TcpClient` → `<DeviceIp>:<KissPort>`, Connect mit Backoff (1s→2s→5s→15s→30s).
2. **KISS-Deframer** (State-Machine, ein Reassembly-Buffer, SLIP-Unescape).
3. `type 0x00` → **AX.25-UI-Decoder**: Adressfeld (6 Zeichen `>>1`, SSID-Byte,
   `ext`-Bit), Digipeater-Liste, `0x03 0xF0`, Info-Feld.
4. `type 0x10` → **RxMeta** puffern, an den **nächsten** Data-Frame hängen.
5. Info-Feld → `MeshcomMessage`:
   - `!` / `=` / `@` / `/` → Position: lat/lon/alt via APRS-Parse; **Kommentar
     komplett** in neues Feld (§5.5).
   - `:` → Nachricht: `:ADDRESSEE :text`.
   - `src` = Origin-Call, `digis` = `RelayPath`, `dest` ignorieren.
6. `ChatService.AddPositionBeacon / AddIncomingMessage / AddAck(...)` mit
   `NodeId` des Workers — **identische Pipeline** wie ext-udp, damit MH-Liste,
   Karte, DB-Sink, Webhooks, MQTT unverändert weiterlaufen.

### 5.4 TX-Routing

`SendMessageAsync(destination, text, tabKey, sourceNodeId)` bleibt die
öffentliche Signatur (`IMeshcomSender`). Intern:

```
Ziel-Node ermitteln (wie heute) →
  Transport == ExtUdp → bisheriger UDP-JSON-Pfad
  Transport == Kiss   → AX.25-UI-Frame bauen:
     src = node.Callsign (Basis-Call MUSS == Node-Call, sonst verwirft der Node),
     dest = "APRS", control 0x03, pid 0xF0,
     info = ":ADDRESSEE :text"  bzw.  "!lat/lon..." für Position
     → KISS-escapen → über die TCP-Verbindung des Node senden
```

Einschränkungen laut Doku: nur Message- und Positions-Payloads; kein
APRS-ACK-Bridging (MeshCom-ACKs bleiben firmware-intern); AX.25-Call max. 6
Zeichen + SSID → lange/taktische Rufzeichen können nicht round-trippen (dann
Frame nicht senden + Log-Hinweis). Node-Ergebnis (akzeptiert / abgelehnt +
Grund) kommt über den TX-Result-Frame `0xF0` zurück (§3.4) — die gesendete
`MeshcomMessage` wird damit auf „ok" (+ `msg_id`) oder „abgelehnt (Grund)"
gesetzt.

### 5.5 Erweiterte Monitor-Infos (Anforderung 2)

`MeshcomMessage` ([MeshcomMessage.cs](../MeshcomWebDesk/Models/MeshcomMessage.cs))
ergänzen:

```csharp
public string? AprsComment      { get; set; }  // volle Kommentarzeile
public string? DigipeaterPath   { get; set; }  // aus AX.25 (KISS) – vs. RelayPath aus JSON
public int?    NeighbourCount   { get; set; }  // /N
public string? RelayNodeList    { get; set; }  // /R=262;2626;...
// /B= /A= füllen bereits Battery/Altitude; /P /H /T → Pressure/Humidity/Temp1
```

Ein kleiner **APRS-Kommentar-Parser** (`/B= /A= /N /P= /H= /T= /R=` extrahieren)
— nützlich für **beide** Transporte, denn auch ext-udp `pos` hat manchmal einen
Kommentar; bislang wertet WebDesk ihn nicht aus.

Monitor-UI ([Chat.razor](../MeshcomWebDesk/Components/Pages/Chat.razor) Positions-
Block ab Zeile 333): zusätzliche Badges nur rendern, wenn Wert vorhanden —
`👥 N9`, Relay-Liste als Chips, Digipeater-Pfad wie der bestehende
`mon-relay-node`-Stil, RSSI/SNR pro Frame (aus RxMeta). MH-Liste
([HeardStation.cs](../MeshcomWebDesk/Models/HeardStation.cs)) um `NeighbourCount`
erweitern.

„Nur wenn Primär-Node auf KISS": Die Zusatzfelder sind schlicht `null` bei
ext-udp — die UI zeigt sie dann nicht. Kein Sonderfall-Code nötig; die
Anforderung ergibt sich automatisch daraus, dass nur der KISS-Pfad sie füllt.

### 5.6 Verbindungsstatus pro Node

Neu: `KissConnectionState` je Node (Disconnected / Connecting / Connected /
NodeGone) + „Single-Client belegt"-Erkennung (Connect refused / sofortiger
Close). In der Node-Switcher-UI und im Monitor-Header anzeigen. `ConnectionStatus`
(app-weit) bleibt für den Primär-Node; ideal wäre langfristig eine
`ConcurrentDictionary<Guid, NodeRuntimeStatus>`.

### 5.7 WebDesk als KISS-Hub (löst den Single-Client-Konflikt)

**Problem:** Der Node akzeptiert auf Port 8001 **genau eine** TCP-Verbindung.
Sobald WebDesk verbunden ist, kommen Direwolf, YAAC, APRSdroid usw. nicht mehr
an den Node — und umgekehrt.

**Lösung:** WebDesk hält die eine Verbindung zum Node und öffnet **selbst einen
KISS/TCP-Listener** auf einem eigenen Port (z. B. `8001` auf dem WebDesk-Host,
konfigurierbar). Andere Apps verbinden sich zu WebDesk, nicht zum Node.

```
                        ┌── Direwolf (IGate)
Node :8001 ── WebDesk ──┼── YAAC (Karte)
  (1 Verbindung)        │   └── eigenes Skript / MQTT-Bridge
                        (KISS-Listener, N Clients)
```

- **RX:** jedes `type 0x00`-Frame vom Node → an alle angedockten Apps fan-out.
- **TX:** `type 0x00`-Frame von irgendeiner App → über die eine Node-Verbindung
  raus (und in WebDescs eigene Anzeige).

Das ist exakt die AGWPE-/KISS-Server-Rolle, die Direwolf schon füllt — als
Einbau in WebDesk „ein paar Dutzend Zeilen": `TcpListener` + Client-Liste +
Fan-out. Bewusst **nicht** im Node gelöst (Node-multi-client): nRF52/W5100S hat
nur 4 Hardware-Sockets (1990 + 1799 + NTP belegt), +N×~600 B RAM, und der Node
soll einfach bleiben. Multiplexing gehört eine Ebene höher — in den Client, der
ohnehin läuft.

**Beim Hub beachten:**

- **Nur `type 0x00` durchreichen.** Port 1 (RxMeta `0x10`) und Port 15
  (TX-Result `0xF0`) sind für WebDesk selbst — WebDesk **konsumiert** sie und
  reicht sie **nicht** an Standard-Clients weiter (verwirren würde es sie nicht,
  aber auswerten kann nur WebDesk). Umgekehrt filtert der Hub von Downstream-Apps
  ankommende Nicht-`0x00`-Frames (SetHardware etc.) weg.
- **Callsign-Gate.** Alle TX laufen über die eine Node-Verbindung unter dem
  Operator-Call-Stamm. Downstream-Apps **müssen dein Rufzeichen** (beliebige
  SSID) verwenden, sonst verwirft der Node den Frame (`0xF0` → `0x02`). Der Hub
  kann das `0xF0`-Ergebnis an die verursachende Downstream-Verbindung
  zurückreichen oder zumindest loggen.
- **TX-Result-Routing.** Kommt `0xF0` als Antwort auf einen von App X
  eingespeisten Frame, muss der Hub wissen, welche Downstream-Verbindung das
  war → dieselbe FIFO-Korrelation wie in §3.4, nur mit „Absender"-Vermerk pro
  Queue-Eintrag.
- **Backpressure.** Langsamer Downstream-Client darf den Node-Read nicht
  blockieren → nicht-blockierende Sends + bounded Queue mit drop-oldest je
  Client.

Priorität: **eigenständiges Feature nach Phase 1/2** (§8) — die
Deframer/Framer/AX.25-Bausteine sind dann schon da, der Hub ist im Wesentlichen
`TcpListener` + Fan-out + die vier Punkte oben.

---

## 6. Offene Punkte / Entscheidungen

| # | Thema | Vorschlag |
|---|---|---|
| 6.1 | **Refactor-Tiefe.** Voller transport-agnostischer Kern vs. `KissClientService` neben dem UDP-Service. | Phase 1 minimal (§8), Kern-Extraktion als Folge-PR, wenn KISS sich bewährt. |
| 6.2 | **AX.25/APRS-Codec.** Eigenimplementierung (~250 Zeilen) vs. NuGet. | Eigenimplementierung, `lib`-artig gekapselt, fail-closed — spiegelt die Firmware-`lib/ax25_aprs`. Kein brauchbares .NET-KISS/AX.25-Paket bekannt. |
| 6.3 | **ext-udp + KISS gleichzeitig am selben Node.** Firmware erlaubt es (getrennte Sockets). | **Geklärt (§3.3):** RX-Transport ist pro Node entweder/oder; der `tele`-Push bleibt immer UDP. `MeshcomUdpService` läuft unverändert weiter und bindet den UDP-Socket auch, wenn alle Nodes KISS sind (für TX). |
| 6.4 | **ext-udp-`tele`-Push bei KISS-Primär-Node.** `SendExtUdpTeleAsync` sendet UDP an den Node. | **Geklärt:** funktioniert weiter über UDP; die Probe matcht auf die aus dem KISS-Positions-Kommentar geparsten Werte (§5.5), nicht auf einen `tele`-Frame. |
| 6.5 | **nRF52-Nodes.** KISS ist dort auskompiliert. | Transport-Auswahl „KISS" für als RAK erkannte Nodes sperren/warnen (`hw_id`). |
| 6.6 | **Single-Client-Konflikt.** WebDesk belegt den einzigen KISS-Slot des Node. | **Hub-Feature (§5.7):** WebDesk öffnet selbst einen KISS-Listener und reicht `type 0x00` an N Downstream-Apps weiter. Eigenständiger Schritt nach Phase 1/2. Bis dahin klar dokumentieren („kein Direwolf parallel"). |
| 6.7 | **RxMeta-Zuordnung.** Reihenfolge Data→Meta laut Doku garantiert, aber TCP-Reassembly muss stimmen. | Meta immer an den unmittelbar vorher dekodierten Data-Frame hängen; kommt kein Meta, bleibt RSSI/SNR `null`. |
| 6.8 | **Dedup / `msg_id` bei RX.** Firmware dedupt vor KISS. Eingehende KISS-Frames haben kein `msg_id` (kein APRS-Feld). | Kein zusätzliches Dedup nötig. Eigene TX bekommen `msg_id` aus dem `0xF0`-Frame (§3.4); nur bei **eingehenden** Frames bleiben `msg_id`-Features im Monitor leer. |
| 6.9 | **Eigene Position.** Kommt die eigene Bake über KISS? Laut Doku ja (`!`-Frame mit `src` = eigener Call). | `SetOwnPosition()` greift wie bei ext-udp. |
| 6.10 | **KISS-TX-Bestätigung.** War offen: erfährt WebDesk, ob der Node einen gesendeten Frame angenommen/verworfen hat? | **Geklärt durch Firmware KISS v1.2** — TX-Result-Frame `0xF0` (§3.4). Status + Grund + `msg_id` pro gesendetem Frame. |
| 6.11 | **APRS-Message-ACK bricht für Standard-Clients.** Der KISS-TX-Pfad der Firmware (`sendMessage()`) wirft bei DMs das Client-`{nn` weg und hängt eine eigene `{node_msgid` an. Ein Standard-Client (PinPoint, Direwolf, APRSdroid) wartet auf `ack<seine-Nummer>`, bekommt `ack<node-Nummer>` → kein Match → Retransmit-Schleife. | **Firmware-Fix:** im KISS-TX-Pfad das mitgesendete `{nn` **behalten** statt renummerieren. Dann matcht der ACK von selbst — für alle KISS-Clients, auch direkt am Node. Ein Hub-seitiger Workaround (`{nn` ⇄ msg_id-Map, ACK-Rewrite) wurde verworfen (fragil, hilft nur über den WebDesk-Hub). **Bis dahin:** Standard-Clients sehen für ausgehende Messages keine ACK-Bestätigung; RX + Positionen + eingehende Messages funktionieren. WebDesk-Nebenfix: ein ACK, der an einen fremden Call (Hub-Client) adressiert ist, markiert nicht mehr fälschlich eigene Chat-Nachrichten. |

---

## 7. Aufwandsschätzung

| Baustein | Zeilen (grob) |
|---|---|
| KISS-SLIP-Deframer/-Framer (inkl. Port 1 RxMeta, Port 15 TX-Result) | ~140 |
| AX.25-UI Decode/Encode (Adressfeld, Pfad, Control/PID) | ~240 |
| APRS-Info-Feld ⇄ `MeshcomMessage` Glue | ~120 |
| APRS-Kommentar-Parser (`/B /A /N /P /H /T /R`) | ~60 |
| `KissClientService` (TCP je Node, Reconnect, RxMeta-Puffer, TX-Result-FIFO) | ~280 |
| TX-Routing nach `NodeProfile.Transport` + `0xF0`-Ergebnisstatus | ~90 |
| `NodeProfile`/`MeshcomSettings` Felder + Migration | ~20 |
| Settings-UI (Transport-Auswahl, Port, KISS-TX-Status) | ~90 |
| Monitor-UI (Zusatz-Badges, RxMeta, Digipeater-Pfad) | ~100 |
| Per-Node-Verbindungsstatus + Node-Switcher-Anzeige | ~120 |
| Übersetzungen (De/En/Es/Fr/It) | ~40 |
| **Summe** | **~1300–1550** + Tests |

Kein Test-Projekt im Repo → Verifikation über die `verify`-Skill (injizierte
Testdaten) plus eine Wegwerf-Konsolen-App, die KISS-Frames gegen den
Deframer/Decoder wirft (Byte-Vektoren aus `kiss_tcp_test.md`, z. B.
`06 D1 FF` → snr +6, rssi −47; `0xF0 01 <msg_id LE>` → TX akzeptiert).

### Geänderte / neue Dateien

- **neu:** `Services/Kiss/KissFraming.cs`, `Ax25Frame.cs`, `AprsInfo.cs`,
  `KissClientService.cs`, `KissHubService.cs` (Phase 4);
  `docs/kiss-mode-analysis.md` (diese Datei)
- `Models/NodeProfile.cs`, `Models/MeshcomSettings.cs` — neue Felder
- `Models/MeshcomMessage.cs`, `Models/HeardStation.cs` — Zusatzfelder
- `Services/MeshcomUdpService.cs` — TX-Routing-Weiche, ggf. Kern-Extraktion
- `Services/NodeManager.cs` — `TransportForNode()`, Per-Node-Status
- `Program.cs` — `KissClientService` als `HostedService`
- `Components/Pages/Settings.razor` — Transport-Auswahl
- `Components/Pages/Chat.razor` — Monitor-Badges
- `Services/Translations/*.cs` — Strings

---

## 8. Empfohlenes Vorgehen (Phasen)

1. **Phase 1 — RX-only, ein KISS-Node.**
   `NodeProfile.Transport`, `KissClientService` (Deframer + AX.25 + APRS),
   Einspeisung in `ChatService`, Settings-Auswahl, Basis-Monitor. Kein TX.
   Deckt „Monitor zeigt Zusatzinfos" komplett ab.
2. **Phase 2 — TX über KISS.** TX-Routing-Weiche, AX.25-Encode, `0xF0`-Result
   auswerten (FIFO-Korrelation, `msg_id` setzen, Status/Grund an die Sendung).
   Beacon/Bot/AutoReply laufen dann auch über KISS-Nodes.
3. **Phase 3 — Politur.** Per-Node-Verbindungsstatus-UI, RxMeta-Badges,
   Digipeater-Pfad-Darstellung, nRF52-Warnung.
4. **Phase 4 — KISS-Hub (§5.7).** `TcpListener` + Fan-out, `type 0x00`-Filter,
   `0xF0`-Routing an die verursachende Downstream-Verbindung, bounded Queues.
   Nutzt die Bausteine aus Phase 1/2. ~150–250 Zeilen.
5. **Optional später.** Kern-Refactor (transport-agnostisch).

---

## 9. Fazit

Machbar, aber **kein reines UI-Feature**: KISS bricht die „ein Socket, per IP
unterschieden"-Annahme des UDP-Service. Der saubere Weg ist eine
transport-agnostische Trennung; der pragmatische Einstieg ist ein separater
`KissClientService`, der in dieselbe `ChatService`-Pipeline einspeist, plus eine
TX-Routing-Weiche nach `NodeProfile.Transport`. Die geforderten Monitor-
Zusatzinfos (voller Kommentar, `/N`, `/R=`, Digipeater-Pfad, Per-Frame RSSI/SNR)
fallen dabei automatisch an — sie stecken im AX.25/APRS-Frame, den WebDesk für
KISS ohnehin dekodieren muss. Der Löwenanteil des Aufwands ist der
AX.25/APRS/KISS-Codec (~450 Zeilen) und die Per-Node-Verbindungsverwaltung.
