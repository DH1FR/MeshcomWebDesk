# Guide: KISS/TCP transport & KISS hub

WebDesk can talk to a MeshCom node in two ways:

| | ext-udp (default) | KISS/TCP |
|---|---|---|
| Protocol | MeshCom EXTUDP JSON, UDP 1799 | KISS framing over TCP 8001 |
| What the node sends | pre-digested `msg` / `pos` / `tele` JSON | the raw received AX.25/APRS frame |
| Node requirement | `--extudp on` + target IP | firmware **v1.4+**, `--kiss on` |

This guide covers **why** you would use KISS, how to enable it, and how to run
the **KISS hub** so other KISS programs (Dire Wolf, YAAC, APRSdroid …) can share
the node.

For the exact wire format see the firmware spec
`docs/kiss_tcp_protocol.md` in the
[MeshCom-Firmware](https://github.com/icssw-org/MeshCom-Firmware) repository.
The design notes for WebDesk's implementation are in
[`kiss-mode-analysis.md`](kiss-mode-analysis.md).

---

## 1. Why KISS?

By the time a frame reaches WebDesk over ext-udp, the node has already parsed it
into JSON and dropped most of the raw APRS detail. Over KISS the node forwards
the **frame as received on RF**, so WebDesk can show and use things ext-udp does
not carry:

- the **full APRS position comment** – the complete MeshCom extension set
  `Name#comment/B=100/A=000827/N9/P=1016.7/H=56.0/T=23.3/R=262;2626;…`,
  not just latitude / longitude / altitude
- the **digipeater path** – which relay nodes actually forwarded the frame
- the **`/R=` relay-node list** and **`/N` neighbour count**, rendered as badges
- **RSSI / SNR for every frame** (needs `--kiss meta on`)
- a **standard AX.25 / APRS** stream that any APRS tool can consume (via the hub)

When you **send** over KISS, every transmission gets a **per-send result** from
the node's `0xF0` frame:

| `0xF0` status | Meaning |
|---|---|
| accepted | queued for LoRa TX, includes the assigned `msg_id` |
| rejected: bad call | the source base callsign is not the node's own call |
| rejected: TX off | `--kiss tx off` on the node |
| rejected: bad frame | malformed, or the message text exceeds the LoRa MTU |
| rejected: rate limit | more than 8 injected frames per second |

This is a genuine transmit confirmation from the node, independent of the
end-to-end ACK / echo-timeout heuristic.

---

## 2. KISS and ext-udp are complementary

KISS is a **narrower** feed. By firmware design it does **not** carry:

- the node's **own** transmissions (its position beacon, its telemetry, messages
  it originated or injected locally)
- **telemetry-only** frames (`type:"tele"`) – structured temp/hum/pressure numbers
- a direct message addressed **to the node itself** (consumed and auto-ACKed
  locally, not relayed)
- HEY / path frames, binary ACK frames

So for a KISS-primary node you normally **leave `--extudp on` as well** and run
both transports in parallel:

| Comes only from ext-udp | Comes from KISS (often richer) |
|---|---|
| the node's own position on the Live Map | other stations' positions / messages / ACKs |
| the structured **Telemetry** panel | the full position comment incl. raw `/T= /H=` |
| the node's firmware / hardware ID in the status bar | digipeater path, `/R=` list, `/N` count |
| injecting external sensor telemetry into the node | RSSI/SNR per frame, `0xF0` TX result |

WebDesk shows this split in the status bar: for a KISS-primary node you get a
**KISS** dot and an **ext-udp** dot. The ext-udp dot goes **yellow** (with a
tooltip) when no ext-udp packet has arrived for over 5 minutes – meaning the
node's own position / telemetry / firmware are currently missing. The Settings
node card shows the same warning.

Frames that arrive over **both** transports are collapsed by WebDesk's normal
deduplication (keyed on `msg_id`, with a `From/To/Text` fallback so a KISS copy
without a `msg_id` still matches its ext-udp twin).

---

## 3. Enable KISS for a node

### On the node (once)

Console (serial / BLE / NET Console) or the MeshCom web UI:

```
--kiss on            # KISS/TCP server up on port 8001  (ESP32 boards only)
--kiss tx on         # accept injected frames  (optional, for sending)
--kiss meta on       # send the RSSI/SNR frame after each data frame  (optional)
--kiss auth on       # require HMAC authentication  (optional, see §4)
```

`--kiss`, `--kiss tx` and `--kiss meta` are also in WebDesk's **Console Command
Helper** (Network group). WebDesk takes whatever the node offers – if `tx` or
`meta` is off on the node, the corresponding feature is simply unavailable.

> The KISS server needs an active **WiFi STA** connection. It does not start on
> Ethernet-only boards or in WiFi-AP mode (ext-udp does).

### In WebDesk

**Settings → the node's card → *Transport*** → **KISS/TCP (+ ext-udp for node's
own data)**. The port is fixed at `8001`. A status line shows the live
connection state (connecting / connected / node unreachable / slot busy /
error).

---

## 4. Authentication

If the node has `--kiss auth on` (and a `--passwd` set), WebDesk authenticates
with the same **HMAC-SHA256 challenge/response** as the NET Console:

1. node sends `NONCE: <32 hex>` in clear text on connect
2. WebDesk replies `HMAC-SHA256(password, nonce)` as 64 hex
3. node replies `OK` or `FAIL`

The password is the **per-node password** field in the node card (the same one
the NET Console uses). Detection is automatic – if the node does not send a
nonce within a few seconds, WebDesk proceeds without auth.

> With `--kiss auth on`, a plain KISS client (Dire Wolf / YAAC) can **no longer
> connect to the node directly**. Point those clients at WebDesk's **KISS hub**
> instead.

---

## 5. KISS hub

A MeshCom node accepts exactly **one** KISS/TCP connection at a time. Once
WebDesk is connected, Dire Wolf / YAAC / APRSdroid cannot reach the node – and
vice versa. The hub solves this: WebDesk holds the single connection and
**re-serves it as its own KISS/TCP listener** for any number of downstream
clients.

```
                              ┌── Dire Wolf (IGate)
 Node :8001 ──── WebDesk ─────┼── YAAC (map)
   (1 connection)             ├── PinPoint APRS
                              └── your own script / MQTT bridge
                              (hub listener, N clients)
```

### Setup

**Settings → 🔀 KISS Hub**:

| Setting | Notes |
|---|---|
| **Active** | starts / stops the listener |
| **Port** | downstream apps connect here (default `8001`) |
| **Node** | whose traffic to serve – default **Primary node** (must be on the KISS transport) |
| **Reachable from** | **This PC only (localhost)** or **LAN – all interfaces**. Choose *LAN* if the downstream app runs on another machine. |

The status line shows `listening on <bind>:<port> · <node> 🟢/🔴 · N client(s): <list>`.

### Behaviour

- **RX** – every `type 0x00` data frame the node receives is fanned out to **all**
  connected clients.
- **TX** – a `type 0x00` frame from a downstream client is injected via the node;
  the node's `0xF0` result is routed back to **that** client only.
- RxMeta (`0x10`), TX-result (`0xF0`) and SrcInfo (`0x20`) frames are **not**
  forwarded; non-`0x00` frames from downstream are ignored.
- Each client has a bounded, drop-oldest queue so one slow reader cannot stall
  the others.

### Downstream client configuration

Point the client at **`<WebDesk-host>:<hub-port>`** in **KISS-over-TCP** mode
(not a serial TNC):

- **Dire Wolf** – `agwpe.conf` / `direwolf.conf`: use a `KISSPORT`/network KISS
  client, or run `kissutil -h <host> -p <port>`.
- **YAAC** – *Configure → Ports → Add → KISS via TCP*, host + port.
- **PinPoint APRS** – *Settings → Connections → TNC → KISS TCP*, host + port.
- Transmit callsign must be **your own callsign** (any SSID) or the node rejects
  the frame (`0x02`). Receiving works regardless.

> ⚠️ The hub has **no authentication**. Only enable *LAN* access on a trusted
> network.

---

## 6. Two-digit SSIDs (`-16` … `-99`)

AX.25 addresses hold only a 4-bit SSID, so a MeshCom origin with an SSID above 15
is clamped to `-15` in the frame's source field. When that happens the firmware
sends a separate **SrcInfo** frame (`0x20`) with the real callsign right before
the data frame. WebDesk applies it as the true sender – for the monitor display
**and** as the reply addressee, so a reply reaches the right station instead of a
non-existent `-15`.

Standard KISS clients ignore this frame, and the hub does not forward it, so
downstream apps (Dire Wolf / YAAC / PinPoint) still see the clamped `-15` for
those stations.

---

## 7. Troubleshooting

| Symptom | Likely cause |
|---|---|
| KISS dot stays **red** | node not reachable on `:8001` – wrong IP, `--kiss off`, not an ESP32 board, or no WiFi-STA on the node |
| KISS dot **"slot busy"** | another KISS client already holds the node's single connection – disconnect it, or use the hub |
| KISS dot **"auth rejected"** | wrong per-node password, or `--passwd` not set on the node |
| Connected, but **very few frames** | normal on a quiet segment: telemetry-only traffic does not cross KISS. Cross-check the WebDesk monitor – if it only shows `TEL` rows, KISS legitimately has little to deliver. |
| The node's **own** position / telemetry / firmware missing | KISS never sends those – keep `--extudp on` and check the ext-udp dot |
| Downstream app (hub) sees **nothing** | hub not set to *LAN* while the app is on another host; or (as above) little KISS-eligible traffic right now. Send a test message from another station to verify. |
| A DM you send **to the node itself** never reaches the hub | by design – the node consumes and auto-ACKs it, it is not relayed over KISS |

Enable `MeshcomWebDesk.Services.Kiss` at `Debug` in `appsettings.json` (already
the default) to see `KISS RX […]` lines and the per-frame decode in the log.

---

**Full Changelog for the feature**: introduced in
[v1.15.0](release-notes/v1.15.0.md).
