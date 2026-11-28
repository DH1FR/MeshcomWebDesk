# The interface

The menu bar at the top takes you to every area of WebDesk:

| Entry | Content |
|---|---|
| **Chat** | Conversations (tabs) with the monitor below |
| **MH** | List of the stations heard most recently |
| **Map** | Live map of the stations |
| **NET Console** | The node's command line in the browser (only visible when switched on in the settings, see [Maintenance](10-maintenance.md)) |
| **Settings** | All settings in the browser |
| **Clear** | Deletes all stored chats, monitor entries and the MH list (with a confirmation) |
| **About** | Version, links, update and backups |
| **QR** | QR code to open WebDesk on a phone or tablet |
| **Help** | This manual |

The version number is shown next to the name. If you use several nodes, you pick the active node in the node switcher
in the header.

## Chat

### Tabs

Every conversation has its **own tab**:

- **Direct messages** – a tab is created automatically for each callsign
- **Groups** – groups appear as `#<group>` tabs. In the settings you can give group numbers names (e.g. `#262` → `DL`)
- **All** – messages to everybody (`*` or `CQCQCQ`)

With **+** you open a new tab, and you can reorder the tabs by **drag & drop** (on a phone by a long press). The order is
kept in your browser. If very many tabs are open, the bar switches to a one-row view that you swipe sideways.

A click on a **callsign** in a message or in the monitor opens that station's tab. When you hover over it, a small **@**
button appears that inserts `@CALLSIGN` into the input field – handy for replies in groups.

### Read and unread

- Tabs with new messages show a **yellow counter**.
- If you open a tab with many unread messages, the view starts at the **New Messages** divider. A button **↑ N unread**
  jumps back there.
- The list follows new messages only while you are at the end. If you scroll up, the position stays put, and
  **↓ N New Messages** shows how many have arrived in the meantime.
- The browser title shows the number of unread messages (`(3) MeshCom WebDesk`) and the icon gets a red dot. A message
  counts as read when you have really looked at it: the tab must be visible and active.

### Delivery indicator

Behind every sent message a symbol shows how far it got:

| Symbol | Meaning |
|---|---|
| ⏳ grey | Waiting for the node's echo (queued) |
| ✓ blue | The node has transmitted over LoRa |
| ✓✓ green | The recipient has confirmed reception |
| ☁️ | Sent to a group or to everybody (no confirmation expected) |
| ☁️✓ | Delivered through a gateway |
| ❌ red | Delivery failed, the node gave up after several retries (firmware 4.40a) |
| 📬 | Held by a mailbox node until the recipient is reachable |

## MH – most recently heard

The **MH** page lists all stations the node has heard, with

- **position** (from GPS packets), **QTH locator** and **distance** to your station
- **signal strength** (RSSI and SNR, coloured: green, yellow, red)
- **battery level** 🔋 (green above 60 %, yellow above 30 %, red below)
- firmware and hardware (as a hint when you hover over the callsign)

Hovering over a callsign (on a phone: tapping it) shows a **station card** with the details and the buttons
**💬 Open chat** and **🔗 aprs.fi**. The search field 🔍 at the top filters by callsign, message text or name. With the
QRZ.com integration switched on, WebDesk also shows the first name and home location of the operator.

## Monitor

Below the chat the **monitor** shows everything the node reports, newest entry on top:

- **Type:** `MSG` message, `POS` position, `TEL` telemetry, `ACK` acknowledgement, `SYS` system
- **Direction:** `RX` received, `TX` sent
- For relayed messages the **full path** over the relays, e.g. `OE1XAR-62 ⟶ DL0VBK-12 → all`
- Colours: green for sent, cyan for positions, purple for telemetry, gold for acknowledgements

With the **divider** you set the ratio of chat and monitor, with **▼/▲** you hide or show the monitor. Your browser
remembers both. The search field 🔍 in the monitor's title bar shows only rows with a given callsign or piece of text.

## Status bar

The bar at the bottom shows at a glance:

- the **UDP status** (🔴 no socket, 🟡 waiting for signal, 🟢 receiving, see [Connecting the node](02-connect-node.md))
- the last reception with sender and signal strength
- the number of sent messages, your callsign and the node's address
- your own position (from the node or from the browser GPS)
- the switches **🔔 / 🔕** for the alert sound and **🔊 / 🔇** for voice announcements

A new direct message plays an alert sound and, if you wish, the browser reads it aloud. Which further alerts exist
(mentions, CQ calls, desktop notifications) is covered in a later chapter.

## On a phone

You can open WebDesk in the phone's browser (QR code in the menu) and install it as an app. On small screens and when
you turn the phone sideways, the view switches automatically to a compact layout with a one-row tab bar.

Continue with [Writing messages](04-writing-messages.md).
