# Connecting the node

WebDesk and the MeshCom node exchange messages through the **EXTUDP protocol**: JSON packets over **UDP, port 1799**.
Both devices must be reachable in the **same network** (LAN or WiFi) for this.

```
MeshCom node  ◄──── UDP 1799 ────►  WebDesk computer
192.168.1.60                         192.168.1.100
(example IP)                         (example IP)
```

> 📋 All IP addresses, ports and callsigns in this manual are **examples**. In your network the addresses look
> different, e.g. `10.0.0.x` or `172.16.x.x`.

## The quick way: setup wizard

If no node is set up yet, WebDesk asks at startup whether to start the **setup wizard**. It guides you through the
setup step by step and, if you like, searches the network for the node. You can also open it at any time under
**Settings → Connection → Nodes** on each node card (button 🧙 *Wizard*).

The wizard changes nothing automatically and sends nothing to the node.

## Setting it up by hand

### Step 1 – On the node: switch on EXTUDP

| Setting on the node | Value |
|---|---|
| **EXTUDP** | on |
| **UDP target IP** | IP address of the WebDesk computer (e.g. `192.168.1.100`) |
| **UDP target port** | `1799` |

On Windows you find the computer's IP with `ipconfig`, on Linux and macOS with `ip a` or `ifconfig`. The exact menus
depend on the firmware version; MeshCom firmware **v4.35 and newer** is supported.

> ⚠️ **Firmware 4.40a.10 and newer:** The node no longer accepts a **broadcast or multicast address** as the target IP
> (`255.255.255.255`, the broadcast of your own network, `224.x.x.x`, `0.0.0.0` or the node's own IP). Enter the fixed IP
> of the WebDesk computer – best with a fixed address or a DHCP reservation in the router.

### Step 2 – In WebDesk: enter the connection

Open **Settings → Connection → Nodes**. At the top are the shared values for **Receiving (UDP)**, below them **one card
per node**.

| Field | Where | Value |
|---|---|---|
| **Listen IP** | Receiving (UDP) | `0.0.0.0` (all network cards) |
| **Listen Port** | Receiving (UDP) | `1799` |
| **Device IP** | Node card | IP address of the node (e.g. `192.168.1.60`) |
| **Device Port** | Node card | `1799` |
| **Callsign** | Node card | your callsign **with SSID**, e.g. `OE1XYZ-2` |

Listen Port and Device Port must match the EXTUDP settings on the node (default `1799`). Then save the settings.

### Step 3 – Open the firewall

**UDP port 1799** must be allowed inbound on the WebDesk computer.

Windows (once, as administrator):

```powershell
netsh advfirewall firewall add rule name="MeshCom WebDesk UDP 1799" protocol=UDP dir=in localport=1799 action=allow
```

Linux with ufw:

```bash
sudo ufw allow 1799/udp
```

## Checking that it works

An indicator in the **status bar** at the bottom shows the state of the UDP connection:

| Display | Meaning |
|---|---|
| 🔴 **No socket** | WebDesk could not open the port (e.g. it is in use, or the Listen IP is wrong) |
| 🟡 **Waiting for signal** | The port is open, but no packet from the node has arrived yet |
| 🟢 **Receiving** | Packets from the node are arriving |

A click on the indicator opens the connection check. Each node card also has a **🔍 Check settings** button: it compares
your entries (unsaved ones too) and tells you what does not fit, for example the format of IP addresses and ports or
whether the **Listen IP** belongs to this computer.

**Typical causes when nothing arrives:**

- The target IP on the node is wrong or a broadcast address (see above)
- The firewall blocks UDP 1799
- The **SSID** in the callsign does not match the node. Packets still arrive, but the status stays yellow and the status
  bar shows **⚠️ Callsign?**. A click on it opens the check.
- Node and computer are not in the same network

## Several nodes

WebDesk can use **several nodes at the same time**. Each node has its own card with name, callsign, IP and port, its own
chat tabs and its own monitor. Exactly one node is the **primary node**: only it drives the MH list, map, beacons,
telemetry and bot, and the OTA update and reboot of the node. You add further nodes at the end of the section, easiest
with **+ Add node with wizard**.

## Node without WiFi: Bluetooth hub

Nodes without WiFi (e.g. Heltec T114) can be connected through the
[MeshCom UDP Bluetooth Hub](https://github.com/DH1FR/MeshComUDPBluetoothHub). The hub presents itself to WebDesk as a
node with EXTUDP. If it runs on the same computer, hub and WebDesk must not use the same port:

| WebDesk setting | Value |
|---|---|
| Device IP | `127.0.0.1` (or the IP of the hub computer) |
| Device Port | `1798` (the hub's "UDP port") |
| Listen IP / Listen Port | `127.0.0.1` or `0.0.0.0` / `1799` |
| Callsign | that of the node, with SSID |

In the hub you connect the node over Bluetooth (disconnect the phone app first) and enter `127.0.0.1:1799` for "WebDesk".

## Additionally: KISS/TCP

WebDesk always talks to a node through EXTUDP. In addition, each node can be connected through its **KISS/TCP
interface** (TCP port 8001) if the firmware supports it. KISS is an **add-on, not a replacement**: both connections run
side by side. You switch it on on the node card under **Advanced → KISS/TCP**. Everything else is in the
[KISS/TCP guide](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/kiss-tcp-guide-en.md).

## Detailed guide

All details, including the net console and the automatic comparison of the node settings, are in the
[node connection guide](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/node-connection-en.md).

Continue with [The interface](03-interface.md).
