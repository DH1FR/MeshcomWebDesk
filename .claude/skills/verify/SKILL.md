---
name: verify
description: Build, launch and drive MeshcomWebDesk locally with injected UDP test data to verify changes end-to-end.
---

# Verify MeshcomWebDesk changes

## Build & launch (isolated — does not touch the real node or data)

```powershell
dotnet build MeshcomWebDesk/MeshcomWebDesk.csproj

# Isolated instance: local UDP loopback, throwaway data/log dirs.
# NOTE: Kestrel ignores the Urls env var (appsettings wins) → app listens on http://0.0.0.0:5162.
$env:Meshcom__ListenIp="127.0.0.1"; $env:Meshcom__ListenPort="17999"
$env:Meshcom__DeviceIp="127.0.0.1"; $env:Meshcom__DevicePort="17998"
$env:Meshcom__DataPath="<tempdir>\data"; $env:Meshcom__LogPath="<tempdir>\logs"
dotnet run --project MeshcomWebDesk --no-build
```

A `SocketException (10054)` right after start is the ICMP echo of the
registration packet sent into the void — harmless, the service keeps running.

## Inject test stations (simulates a MeshCom node)

Send UDP JSON packets to `127.0.0.1:17999`. Formats (see MeshcomUdpService.ParseMessage):

```
pos    {"type":"pos","src":"DL1AAA-1","lat":51.05,"lat_dir":"N","long":9.55,"long_dir":"E","rssi":-108,"snr":-4.0}
msg    {"type":"msg","src":"DL2BBB-2","dst":"*","msg":"Hello","rssi":-95,"snr":6.5}
tele   {"type":"tele","src":"DL1AAA-1","temp1":21.5,"hum":60,"qnh":1013.2}
```

- Relayed packet: comma path in `src` → `"src":"ORIGIN-1,RELAY-12"` (index 0 = origin).
- Own position: `pos` with `src` == `Meshcom:MyCallsign` (appsettings: `DH1FR-2`).
- `alt` is in feet (APRS convention), converted to metres.
- A `msg` (not pos/tele) without relay path sets `DirectLinkConfirmed` → direct map line.
- `lat:0, lon:0` = no GPS fix (ignored).

## Drive the UI

Playwright (chromium) against `http://127.0.0.1:5162`. Useful hooks on `/map`:

- `window.meshcomMap.jumpToCallsign('DL1AAA-1')` — opens a marker popup (search path);
  clicking marker labels directly fails (`pointer-events: none` on `.aprs-wrap`).
- Relay segments: `document.querySelectorAll('.leaflet-overlay-pane path')`
  (each segment = halo + coloured line, so 2 paths per segment).
- Direction arrows: `.relay-arrow`; marker dots/labels: `.aprs-dot` / `.aprs-label`.
- Wait ~4 s after load: Blazor circuit + 400 ms marker debounce.

Gotcha: the map page is `@rendermode InteractiveServer` — a plain HTTP GET only
proves routing; JS behaviour needs the browser.
