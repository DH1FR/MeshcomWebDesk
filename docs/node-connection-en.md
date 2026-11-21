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

> ⚠️ **Firmware 4.40a.10 and later:** the UDP target IP must **not be a broadcast/multicast address** any more (`255.255.255.255`, the broadcast of the node's own subnet, `224.x.x.x`, `0.0.0.0`, the node's own IP). The node then logs `[EXT] target … refused` and sends nothing. Always enter the **specific IP of the WebDesk PC** (`--extudpip 192.168.1.100`).

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

## Connection Check and Setup Wizard

**Settings → 🔌 Connection** has two buttons:

- **🔍 Check settings** checks the current values (unsaved ones too) and tells you what does not fit and what to change: format of IP addresses and ports, whether the **Listen IP** belongs to this PC (a frequent mistake: the node's IP instead of `0.0.0.0`), whether the UDP socket is open, whether the node is reachable, whether packets arrive and whether **callsign and SSID** match the node.
- **🧙 Setup wizard** guides you step by step (WiFi node, Bluetooth hub on this PC or on another machine) Right in the first step you choose *Configure node automatically* (preselected; WebDesk sets ext UDP and the target IP over the net console; if it is switched off the wizard guides you to switch it on in the node's web interface) or *Configure node manually* (commands to copy). Values are only entered after you click *Apply to form*; you still have to save afterwards.

Neither changes **anything automatically** and neither sends **anything to the node**. Under **Additional Nodes** every node has its own check button (🔍) and wizard (🧙).

If the **UDP status in the status bar is red** or yellow, clicking it opens the check (red: straight to the settings with the concrete error reason).

**First start:** If no node is set up yet (no node list and no data from a node within a few seconds after start; also after all nodes were deleted) WebDesk asks right at startup whether to start the setup wizard (*Yes, start the wizard* / *Later* / *Do not ask again*; once per browser tab). *Later* leaves a banner with *Start setup* at the top of the chat as a reminder; it opens the settings with the wizard. *Do not ask again* / *Do not show again* hides the question and the banner permanently. After the first save it no longer appears.

**Finding a node on the network:** In the wizard's node step the search starts automatically; the results are in a **selection list** at the IP field (*Search again* repeats it). The search resolves the callsign as hostname (the firmware uses it as DHCP/mDNS name), lists IPs that already sent packets and asks the addresses of your local /24 network for a MeshCom web server (port 80, page title "CALLSIGN - Meshcom"). This only finds nodes with the web server switched on; a pure ext-UDP node does not announce itself. Clicking a result only copies the IP into the wizard.

**Reading and correcting settings on the node (net console):** If the node's net console is switched on (port 2323, HMAC password), the check shows the section *Node settings via the net console* below. *Read from node* reports ext UDP, the target IP and the node's callsign. If something differs, *Correct on the node …* offers the commands `--extudp on` and `--extudpip <this PC's IP>`; nothing is sent until you confirm, and only these two commands. The callsign on the node is never changed; instead *Use callsign … in WebDesk* copies the node's value into the form. If the net console is not reachable, the check tells you it must be switched on at the node (web interface: IP Network Settings → net console, or `--netconsole on`; password with `--passwd`).

**Automatic comparison of all node settings:** If the net console is reachable and the node password is known, the check reads the node by itself on every run (read-only, `--info`) and compares what WebDesk is configured for: **callsign/SSID**, **ext UDP** (on) and **target IP** (this PC's IP), the **node's IP address** (against the Device IP) and, when KISS is enabled, **KISS/TCP, TX, RxMeta and Auth**. It also shows whether `--nopmother` withholds foreign direct messages. The section below the check shows everything as a table (*On the node* / *Expected*). The correction only offers `--extudp on`, `--extudpip`, `--kiss on`, `--kiss tx on` and `--kiss meta on`.

**Activating KISS on the node:** If KISS/TCP is switched on under *Connection* (or for an additional node), the button *Activate KISS on the node via the net console* appears there. It reads the node and offers `--kiss on`, `--kiss tx on` and `--kiss meta on` – nothing is sent until you confirm. Only ESP32 nodes with suitable firmware offer KISS.

**Taking over the callsign on either side:** If the callsign differs there are two buttons. *Use callsign … in WebDesk* enters the node's value into the form (then save). *Change callsign on the node to …* sets the node to the callsign from WebDesk (`--setcall`, over the net console). This only happens after a click and confirmation; the warning points out that the node's identity in the mesh changes and that the node may restart. Only this one command is sent.

**Noticing a wrong SSID right away:** A mismatch is also noticed without the net console: if WebDesk sends a message and the node returns it under a different callsign, the status bar shows the warning badge **⚠️ Callsign?** (a click opens the check) and the check names the callsign the node really uses. WebDesk changes nothing automatically.

**Callsign and SSID:** WebDesk recognises its own packets by the callsign. If the SSID differs from the node, packets arrive but the UDP status stays yellow. The check compares with what the node reports itself (position, telemetry, message echo or `--info` via the net console) and names the correct value.

---

## Bluetooth Hub (MeshCom UDP Bluetooth Hub)

Nodes without WiFi (e.g. Heltec T114) can be connected through the **MeshCom UDP Bluetooth Hub**. The hub presents itself to WebDesk as a node with ext-UDP. When it runs on the same PC, hub and WebDesk must not use the same port:

| WebDesk setting | Value |
|---|---|
| Device IP | `127.0.0.1` (or the hub machine's IP) |
| Device Port | `1798` (the hub's "UDP port") |
| Listen IP / Listen Port | `127.0.0.1` or `0.0.0.0` / `1799` |
| Callsign | **the node's**, with SSID |

In the hub: connect the node via Bluetooth (disconnect the phone app first), "WebDesk" = `127.0.0.1:1799`. The connection check recognises hub operation, checks whether something listens on the hub port and warns about port conflicts. WebDesk cannot tell **two nodes with the same IP** apart (e.g. two hubs on one PC) because it assigns packets by their sender IP.

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
