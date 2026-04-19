# 🔌 MeshCom Node: Connection Settings for WebDesk

This guide explains which settings need to be configured on the **MeshCom node** and in **WebDesk** to establish communication.

---

## Prerequisite: Same Network

The node and the WebDesk PC must be reachable in the **same local network (LAN/Wi-Fi)**.

> 📋 **Note on example values:** All IP addresses, ports and callsigns in this guide are **examples only**. In your network the addresses may look completely different (e.g. `10.0.0.x`, `172.16.x.x` or another subnet). Adjust all values to match your own network configuration.

```
MeshCom Node  ◄──── UDP 1799 ────►  WebDesk PC
192.168.1.60                         192.168.1.100
(example IP)                         (example IP)
```

---

## Step 1 – Configure Node Firmware (EXTUDP)

WebDesk communicates via the **EXTUDP JSON protocol**. This must be enabled on the node.

> ℹ️ The exact menu paths depend on the firmware version.  
> Compatible with **MeshCom Firmware v4.35+**

| Setting in Node Menu | Value |
|----------------------|-------|
| **Enable EXTUDP** | ✅ On |
| **UDP Target IP** | IP address of the WebDesk PC (e.g. `192.168.1.100`) |
| **UDP Target Port** | `1799` |

> 💡 **Tip:** Find the WebDesk PC's IP with `ipconfig` (Windows) or `ip a` / `ifconfig` (Linux/macOS).

---

## Step 2 – WebDesk Settings (`/settings` → 🔌 Connection)

| Field | Recommended Value | Meaning |
|-------|------------------|---------|
| **Listen IP** | `0.0.0.0` | Listens on all network interfaces |
| **Listen Port** | `1799` | UDP port on which WebDesk receives incoming messages |
| **Device IP** | `192.168.1.60` | IP address of the MeshCom node |
| **Device Port** | `1799` | UDP port of the node for outgoing messages |
| **Own Callsign** | e.g. `G0XYZ-2` | Your callsign including SSID |

> ⚠️ **Listen Port** and **Device Port** must match the EXTUDP configuration of the node (default: `1799`).

---

## Step 3 – Check Firewall

Make sure **UDP port 1799** is **not blocked** on the WebDesk PC.

**Windows Firewall (once, as Administrator):**
```powershell
netsh advfirewall firewall add rule name="MeshCom WebDesk UDP 1799" protocol=UDP dir=in localport=1799 action=allow
```

**Linux (ufw):**
```bash
sudo ufw allow 1799/udp
```

---

## Monitor Filter

The **monitor pane** (lower area) has a search field 🔍 in its title bar.

- Type any **callsign** or **text fragment** to instantly filter the rows
- Searches: `From`, `To`, `message text`, `raw data`
- **Case-insensitive**, position in text does not matter
- The counter switches to `X / Y Entries` while a filter is active
- The **×** button clears the filter

---

## Status Bar Indicator

| Symbol | Meaning |
|--------|---------|
| 🔴 UDP **No socket** | UDP port could not be bound (e.g. port already in use) |
| 🟡 UDP **Waiting for signal** | Socket active, but no packet received from the node yet |
| 🟢 UDP **Receiving** | At least one UDP packet has been received from the node |

> ℹ️ Since UDP is connectionless, there is no classic "connected" state. Green means data is actually arriving.

---

## Common Problems

| Symptom | Possible Cause |
|---------|---------------|
| Status: 🔴 No socket | UDP port already in use or permission error |
| Status: 🟡 Waiting for signal | Node is not sending to the correct IP / wrong port |
| No incoming messages | Firewall blocking UDP 1799 |
| Can send but not receive | Listen IP incorrect (e.g. specific IP instead of `0.0.0.0`) |
| Can receive but not send | Device IP or Device Port incorrect |
| Messages arriving twice | Node is configured with two target IPs |

---

## Example: Complete Configuration

```
Node IP:          192.168.1.60
WebDesk PC IP:    192.168.1.100

--- Node Firmware ---
EXTUDP:           Enabled
UDP Target IP:    192.168.1.100  ← WebDesk PC
UDP Target Port:  1799

--- WebDesk appsettings / Settings page ---
ListenIp:         0.0.0.0
ListenPort:       1799
DeviceIp:         192.168.1.60   ← Node
DevicePort:       1799
MyCallsign:       G0XYZ-2
```

---

## Further Links

- 🏠 [MeshCom Project](https://icssw.org/meshcom/)
- 💾 [MeshCom Firmware on GitHub](https://github.com/icssw-org/MeshCom-Firmware)
- 🔗 [MeshCom WebDesk Repository](https://github.com/DH1FR/MeshcomWebDesk)
- 📖 [Home Assistant Telemetry Integration](homeassistant-telemetry.md)
