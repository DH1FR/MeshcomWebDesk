# MeshCom Web Client

A **Blazor Server** web application for communicating with a [MeshCom 4.0](https://icssw.org/meshcom/) node via UDP (EXTUDP JSON protocol).  
Built with **.NET 10** and **Blazor Interactive Server**.

> **MeshCom Firmware:** Compatible with [icssw-org/MeshCom-Firmware](https://github.com/icssw-org/MeshCom-Firmware) v4.35+

---

## 💡 Motivation

MeshCom always reminds me a little of the good old **Packet Radio** days – digital text communication over radio, simple and direct.

However, I could not find any suitable software that provides a **web server** interface for MeshCom accessible from any device (PC, tablet, smartphone) within the local network. That is why I created this **MeshCom Web Client**.

The application runs on **Windows** or **Linux** and makes a full web client for MeshCom available via a simple URL – no installation required on the end device, everything runs directly in the browser.

---

## Screenshots

![MeshCom Web Client](docs/screenshot.png)

---

## Features

### 💬 Chat
- **Multi-tab conversations** – each partner (callsign, group, broadcast) gets its own tab
- **Broadcast tab "All"** for `*` / `CQCQCQ` messages
- **Direct messages** – each callsign gets its own tab automatically
- **Group messages** – group destinations appear as `#<group>` tabs with optional whitelist filter
- Smart routing: broadcast replies from a known callsign appear in their direct tab
- **Auto-scroll** to the latest message when a tab is opened or a new message arrives
- **Unread badge** – inactive tabs show a yellow counter badge for new messages
- **ACK delivery indicator** on every outgoing message:
  - `⏳` grey – waiting for node echo (message queued)
  - `✓` blue – node has transmitted over LoRa (sequence number assigned)
  - `✓✓` green – recipient confirmed delivery (APRS ACK received)
- **Clickable callsigns in the monitor** – click any sender or recipient to open a chat tab instantly
- **Audio notification** 🔔 when a new direct message to your own callsign arrives (Web Audio API, no audio file required); mute toggle in the status bar

### 📻 MH – Most Recently Heard
- Live table of all heard stations with last message, timestamp and message count
- **GPS position** parsed from EXTUDP position packets (`lat_dir` / `long_dir` APRS format)
- **Distance calculation** (Haversine) from own position to each heard station
- **Battery level** 🔋 column parsed from `batt` field in position/telemetry packets, colour-coded (🟢 >60% / 🟡 >30% / 🔴 ≤30%)
- **Hardware badge** – short hardware name from `hw_id` field (e.g. `T-BEAM`, `T-ECHO`, `HELTEC-V3`)
- **Firmware tooltip** – hover the callsign to see firmware version, hardware ID and first-heard time
- **RSSI / SNR** signal quality with colour coding (green / yellow / red)
- Altitude correctly converted from APRS feet to metres
- 🗺️ OpenStreetMap link per station
- Own position extracted automatically from the node's `type:"pos"` UDP beacon
- **Browser GPS** button to use device geolocation as own position
- Click 💬 to open a chat tab with any station

### 📡 Monitor (lower pane)
- Structured display with type badge (`MSG` / `POS` / `TEL` / `ACK` / `SYS`), direction (`RX` / `TX`), routing and signal
- **Full relay path** shown inline for relayed messages: `OE1XAR-62 ⟶ DL0VBK-12 ⟶ DB0KH-11 → all`
- **Telemetry rows** (`type:"tele"`) display temperature 🌡️, humidity 💧, pressure 🧭 and battery 🔋
- Colour-coded rows: green for TX, cyan for position beacons, purple for telemetry, gold for ACKs
- Newest entry always at the top; configurable history limit (`MonitorMaxMessages`)
- Collapsible on mobile (toggle button)

### 📊 Status bar
- UDP socket state (🟢 Active / 🔴 Inactive) and registration status
- Last RX timestamp, sender callsign, RSSI / SNR with colour coding
- TX counter, own callsign, device IP:Port
- Own GPS position with source label (Node / Browser GPS)
- 🔔 / 🔕 Sound notification toggle

### 🔄 Deduplication
- Incoming messages are deduplicated using the `msg_id` field (unique hex ID from the node)
- Fallback chain: `msg_id` → `{NNN}` sequence number → message text
- Duplicate suppression window: 10 minutes (rolling cache, auto-pruned)

### 💾 State Persistence
- Chat tabs, MH list, monitor history and **own GPS position** are saved to disk on shutdown
- State is restored automatically on startup – no waiting for the first position beacon
- Auto-save every 5 minutes; data stored in `DataPath` (configurable)

### ℹ️ About page
- Displays assembly version (e.g. `v1.0.1`), build timestamp and links

### ⚙️ Settings page
- Web-based configuration editor at `/settings` – edit all `appsettings.json` values directly in the browser
- Changes are saved to `appsettings.json` on disk; most settings apply **immediately without restart**
- Settings that still require a restart: **Listen-IP / Listen-Port** (socket binding) and **Log-Path / Log-Retention** (Serilog)

### 📡 Beacon (Bake)
- **Periodic beacon** – sends a configurable text to a configurable group at a fixed interval
- Interval is configurable in whole hours (minimum 1 h); first transmission after one full interval
- Enabled / disabled via `BeaconEnabled` flag – applies **live** without restart
- **Status indicator** in the status bar: pulsing `●` dot with next scheduled send time; turns yellow when < 10 min away
- Beacon appears in the monitor feed and in the corresponding group chat tab

### 📝 Logging (Serilog)
- Rolling daily log files with configurable retention
- Optional UDP traffic log (`LogUdpTraffic`) for offline analysis

---

## Architecture

```
MeshcomWebClient/              ← Blazor Server (ASP.NET Core host)
│  Program.cs                  ← DI setup, Serilog, hosted services
│  appsettings.json            ← All configuration
│
├─ Components/
│  ├─ App.razor                ← HTML shell + JS helpers (scrollToBottom, playNotificationBeep)
│  ├─ Layout/
│  │    MainLayout.razor       ← Top navigation bar
│  └─ Pages/
│       Chat.razor             ← Chat tabs + monitor pane + status bar
│       Mh.razor               ← Most Recently Heard table + own position
│       Settings.razor         ← Web-based configuration editor
│       About.razor            ← Version / copyright / build info
│       Clear.razor            ← Data reset page
│
├─ Helpers/
│     GeoHelper.cs             ← Haversine, coordinate formatting, OSM links
│     MeshcomLookup.cs         ← hw_id → hardware name table, firmware formatter
│
├─ Models/
│     MeshcomMessage.cs        ← Message model (from/to/text/GPS/RSSI/ACK/relay/telemetry)
│     MeshcomSettings.cs       ← Strongly-typed config (IOptions)
│     ChatTab.cs               ← Tab model with UnreadCount
│     HeardStation.cs          ← MH list entry (GPS, signal, battery, hardware, firmware)
│     ConnectionStatus.cs      ← Live UDP status + own GPS position
│     PersistenceSnapshot.cs   ← Serialisable state snapshot (tabs, MH, monitor, own GPS)
│
└─ Services/
      MeshcomUdpService.cs     ← BackgroundService: UDP RX/TX, JSON parsing, ACK matching, beacon timer
      ChatService.cs           ← Singleton: routing, tabs, MH list, monitor, deduplication
      DataPersistenceService.cs← BackgroundService: load/save state to JSON on disk
      SettingsService.cs       ← Reads/writes appsettings.json; changes applied live via IOptionsMonitor
```

---

## Configuration

All settings in `MeshcomWebClient/appsettings.json`:

```json
"Meshcom": {
  "ListenIp":           "0.0.0.0",       // bind address (0.0.0.0 = all interfaces)
  "ListenPort":         1799,            // local UDP port
  "DeviceIp":           "192.168.1.60",  // MeshCom node IP
  "DevicePort":         1799,            // MeshCom node UDP port
  "MyCallsign":         "NOCALL-1",       // own callsign
  "LogPath":            "C:\\Temp\\Logs",// log file directory
  "LogRetainDays":      30,              // log file retention in days
  "LogUdpTraffic":      false,           // log every UDP packet to file
  "MonitorMaxMessages": 1000,            // max monitor history (oldest dropped)
  "GroupFilterEnabled": true,            // only show whitelisted group tabs
  "Groups":             ["#20","#262"],  // whitelisted groups (GroupFilterEnabled=true)
  "DataPath":           "C:\\Temp\\MeshcomData", // persistent state directory
  "AutoReplyEnabled":   false,           // send auto-reply on first contact
  "AutoReplyText":      "...",           // auto-reply message text
  "BeaconEnabled":      false,           // send periodic beacon (Bake)
  "BeaconGroup":        "#262",          // target group for beacon
  "BeaconText":         "...",           // beacon text
  "BeaconIntervalHours": 1               // beacon interval in hours (minimum 1)
}
```

### LAN access (iPad / mobile)

The `lan` launch profile binds to all network interfaces:

```powershell
# In Visual Studio: select profile "lan" next to the Run button
# Then open in browser on any device in the same network:
http://192.168.x.x:5162
```

### UDP traffic logging

Set `"LogUdpTraffic": true` to write every packet to the log file:

```
[INF] [UDP-RX] 192.168.1.60:1799 {"src_type":"lora","type":"msg","src":"DH1FR-1",...}
[INF] [UDP-TX] 192.168.1.60:1799 {"type":"msg","dst":"DH1FR-1","msg":"Hello"}
```

Filter the log file:
```powershell
Select-String "\[UDP-RX\]" C:\Temp\Logs\MeshcomWebClient-*.log
Select-String "\[UDP-TX\]" C:\Temp\Logs\MeshcomWebClient-*.log
```

---

## EXTUDP Protocol

This client communicates with the MeshCom node using the **EXTUDP JSON protocol** defined in the [MeshCom firmware](https://github.com/icssw-org/MeshCom-Firmware).

### Packet types

| `type` | Description | Handled as |
|--------|-------------|------------|
| `msg`  | Chat message (direct, broadcast, group, ACK) | Chat tab + monitor |
| `pos`  | Position beacon with GPS coordinates | MH list + monitor |
| `tele` | Telemetry (temperature, humidity, pressure, battery) | MH list + monitor |

### Example packets

| Direction | Example |
|-----------|---------|
| Registration | `{"type":"info","src":"NOCALL-2","dst":"*","msg":"info"}` |
| Chat RX (direct) | `{"src_type":"lora","type":"msg","src":"NOCALL-1","dst":"NOCALL-2","msg":"Hello{034","msg_id":"5DFC7187","rssi":-95,"snr":12,"firmware":35,"fw_sub":"p"}` |
| Chat RX (relayed) | `{"src_type":"lora","type":"msg","src":"OE1XAR-62,DL0VBK-12,DB0KH-11","dst":"*","msg":"...","rssi":-109,"snr":5}` |
| Position RX | `{"src_type":"lora","type":"pos","src":"DB0MGN-1,...","lat":50.57,"lat_dir":"N","long":10.42,"long_dir":"E","alt":1243,"batt":100,"hw_id":42,"firmware":35,"fw_sub":"p","rssi":-108,"snr":5}` |
| Telemetry RX | `{"src_type":"lora","type":"tele","src":"DB0MGN-1,...","batt":100,"temp1":20.6,"hum":0,"qnh":1031.4}` |
| Chat TX | `{"type":"msg","dst":"NOCALL-1","msg":"Hello"}` |
| ACK RX | `{"src_type":"udp","type":"msg","src":"NOCALL-1","dst":"NOCALL-2","msg":"NOCALL-2  :ack034","msg_id":"A177E139"}` |

### ACK delivery tracking

1. Outgoing message sent → `⏳` pending
2. Node echo arrives with sequence marker `{034}` → stored, indicator changes to `✓`
3. Recipient sends APRS ACK `:ack034` → message marked as delivered `✓✓`

### Hardware IDs (`hw_id`)

| ID | Short name | Hardware |
|----|-----------|---------|
| 1–3 | TLORA-V1/V2 | TTGO LoRa32 |
| 4–6, 12 | T-BEAM | TTGO T-Beam |
| 7 | T-ECHO | LilyGO T-Echo |
| 8 | T-DECK | LilyGO T-Deck |
| 9 | RAK4631 | Wisblock RAK4631 |
| 10–11, 43 | HELTEC-V1/V2/V3 | Heltec WiFi LoRa 32 |
| 39 | EBYTE-E22 | Ebyte LoRa E22 |

> **Note:** Altitude in position packets follows APRS convention (feet). The client converts to metres automatically.

---

## Requirements

> 💡 **No build required:** Ready-to-run binaries for Windows and Linux are available under [Releases](https://github.com/DH1FR/MeshcomWebClient/releases/latest).

- [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) *(ASP.NET Core Runtime, required to run the Windows binary)*
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) *(only required for build from source)*
- A reachable MeshCom node running firmware [v4.35+](https://github.com/icssw-org/MeshCom-Firmware/releases) with EXTUDP enabled
- UDP port 1799 open (Windows Firewall / router)

### ⚠️ Windows SmartScreen warning

When running the `.exe` for the first time, Windows may show **"Windows protected your PC"**.  
This happens because the binary is not code-signed.

**To run it anyway:**
1. Click **"More info"** in the SmartScreen dialog
2. Click **"Run anyway"**

**Alternative:** Right-click the `.exe` → **Properties** → check **"Unblock"** → OK

---

## Build & Run

```powershell
cd MeshcomWebClient
dotnet run --launch-profile lan    # accessible from all devices in the LAN
# or
dotnet run                         # localhost only
```

Then open `http://localhost:5162` (or `http://<your-ip>:5162` for LAN access).

---

## 🐳 Docker – Deployment on Linux

### Prerequisites

```bash
# Install Docker + Docker Compose plugin (Debian / Ubuntu / Raspberry Pi OS)
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-plugin

# Add current user to the docker group (no sudo needed)
sudo usermod -aG docker $USER
newgrp docker
```

### Initial setup & start

```bash
# Clone repository
git clone https://github.com/DH1FR/MeshcomWebClient.git
cd MeshcomWebClient

# Create optional config file (overrides embedded defaults)
cp deploy/appsettings.linux.json appsettings.json
nano appsettings.json          # set DeviceIp, MyCallsign, Groups etc.

# Build image and start container
docker compose up -d --build
```

The container runs in the background and restarts automatically (`restart: unless-stopped`).  
Web interface: **http://\<Linux-IP\>:5162**

> **Note:** `network_mode: host` is required so the container can receive UDP packets from the MeshCom device.

### Changing the configuration

Either edit `appsettings.json` (next to `docker-compose.yml`) or use environment variables in `docker-compose.yml`:

```yaml
environment:
  - Meshcom__DeviceIp=192.168.1.60
  - Meshcom__MyCallsign=NOCALL-1
  - Meshcom__GroupFilterEnabled=true
  - Meshcom__Groups__0=#OE
  - Meshcom__Groups__1=#Test
```

After any change to `docker-compose.yml` or `appsettings.json`:

```bash
docker compose up -d
```

---

### 🔄 Updating to a new version

Pull the latest changes, rebuild the image and replace the container:

```bash
cd MeshcomWebClient

# Fetch latest changes
git pull origin master

# Rebuild image and replace container (brief downtime)
docker compose up -d --build

# Remove unused old image (optional)
docker image prune -f
```

### Useful Docker commands

```bash
# Check container status
docker compose ps

# Follow live logs (Ctrl+C to exit)
docker compose logs -f

# Stop container
docker compose stop

# Stop and remove container (config & logs are preserved)
docker compose down

# Stop, remove container and delete image (full reset)
docker compose down --rmi local
```

---

## 💻 Direct installation (without Docker)

Docker is the recommended deployment method. If you prefer not to use Docker, download the binary directly – it is **framework-dependent**, meaning the **.NET 10 Runtime** must be installed on the target machine (no SDK needed).

> 📦 **Download:** [GitHub Releases](https://github.com/DH1FR/MeshcomWebClient/releases/latest)

---

### Windows

**Prerequisites:**
- [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
# Unzip to e.g. C:\meshcom
Expand-Archive MeshcomWebClient-vX.Y.Z-win-x64.zip -DestinationPath C:\meshcom

# Edit configuration
notepad C:\meshcom\appsettings.json   # set DeviceIp, MyCallsign

# Start
cd C:\meshcom
.\MeshcomWebClient.exe
```

Open browser: **http://localhost:5162**

> To run automatically at Windows startup, register as a Windows service:
> ```powershell
> sc.exe create MeshcomWebClient binPath="C:\meshcom\MeshcomWebClient.exe" start=auto
> sc.exe start MeshcomWebClient
> ```

---

### Linux (systemd)

**Prerequisites:**
- [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

```bash
# Install .NET 10 Runtime (Debian / Ubuntu / Raspberry Pi OS)
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0

# Extract archive
mkdir meshcom && tar -xzf MeshcomWebClient-vX.Y.Z-linux-x64.tar.gz -C meshcom
cd meshcom

# Edit configuration (MyCallsign, DeviceIp etc.)
nano appsettings.json

# Install as systemd service (starts automatically at boot)
sudo bash install.sh
```

Web interface: **http://\<Linux-IP\>:5162**

**Useful commands after installation:**
```bash
journalctl -u meshcom-webclient -f     # live log
systemctl status meshcom-webclient     # status
systemctl restart meshcom-webclient    # restart after config change
```

---

### Configuration reference

The shipped `appsettings.json` contains placeholder values – the following **must** be set before first start:

| Key | Description | Example |
|-----|-------------|---------|
| `MyCallsign` | Your own callsign | `NOCALL-1` |
| `DeviceIp` | IP address of the MeshCom node | `192.168.1.60` |
| `LogPath` | Directory for log files | `./logs` / `/var/log/meshcom` |
| `DataPath` | Directory for persistent state | `./data` / `/opt/meshcom/data` |

---

## ⚖️ Legal

### Copyright
© 2025–2026 Ralf Altenbrand (DH1FR) · All rights reserved.

### Usage
This software is made available for **licensed radio amateurs** for **private, non-commercial use**.  
Commercial use is not permitted without explicit written consent from the author.

### Disclaimer
**Use at your own risk.**  
The author accepts no liability for damages of any kind – including but not limited to damage to hardware, network infrastructure, radio equipment or data loss – caused by the use of this software.  
The software is provided without any warranty.

### License
See [LICENSE](LICENSE)

---

© by Ralf Altenbrand (DH1FR) 2025–2026
