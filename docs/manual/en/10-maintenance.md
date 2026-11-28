# Backup, maintenance and troubleshooting

## Where WebDesk keeps its data

| What | Where |
|---|---|
| Settings you save in the interface | `<DataPath>/appsettings.override.json` |
| Chats, MH list and monitor | in the folder `<DataPath>` (WebDesk saves on exit and every 5 minutes) |
| Access protection password (hash) | `<DataPath>/access.json` |
| Keys | `<DataPath>/keys` |
| Log files | in the folder `logs` (adjustable with `LogPath`) |
| Additional language files | `<DataPath>/languages/` |

Without your own setting `data/`, `logs/` and the keys are **next to the program**. With Docker they are the folders `./data`,
`./logs` and `./keys` next to `docker-compose.yml`. On exit WebDesk saves its state and restores it at the next start.
Settings you change in the interface are written to the file `appsettings.override.json`. The supplied `appsettings.json`
stays unchanged (with Docker it may be read-only).

## Backing up and restoring settings

Under **Settings → System → Backup & Restore** (at the very bottom) you back up **all settings** into an encrypted file:

- You choose a **password**. The file is called `MeshComWebDesk-settings.enc`; you can change the name before the export.
- It also contains passwords and keys (QRZ.com, MQTT, AI, database …), encrypted with **AES-256**. It can be restored on
  another computer or operating system.
- **Restoring:** pick the file, enter the password, WebDesk checks it, saves the settings and reloads the page. A wrong
  password, a damaged file or missing details produce a clear error message.

> ⚠️ **The password is not stored anywhere.** If it is lost, the file can no longer be opened.

The access protection password (`access.json`, see [Security](09-security.md)) is not part of this backup.

## Updating

Updating is covered in the chapter [Quick start and installation](01-install.md). Without Docker WebDesk updates itself on the
page **About → Update / backups** and creates a backup first.

## Deleting data

The **Clear** page removes **all stored chats, monitor entries and the MH list**. This cannot be undone; WebDesk asks first.
Your settings stay unchanged.

## Console: operating the node directly

The **NET Console** page (`/telnet`) gives you the node's command line in the browser. It appears in the menu bar once you
switch the console on under **Settings → Connection → Console**. There you also choose the way:

- **TLS console:** over the network, encrypted. Handy when the node cannot be reached over USB (e.g. outdoors). It needs firmware
  **v4.35p.05.13 or newer**. On the node you switch it on with `--tlsconsole on` and set a password with `--passwd <password>`.
  In WebDesk you enter the device IP and the password. On the first connect WebDesk shows the node's certificate; you accept it
  with **Trust & Save**.
- **Serial console (USB):** directly on the node's USB port. You choose port and baud rate. On Windows you use the plain name
  (`COM3`), on Linux and Docker you pass the device (`/dev/ttyUSB0`) through to the container.

The console has buttons to **pause** the display, **clear** the display, for an **OTA update** of the node (with a
countdown) and to **reboot** the node (with a confirmation). The **command helper** helps you find the node's commands.

## Log files

WebDesk writes daily log files into the folder `logs` (how long they are kept you set under **Settings → System → Logging**).
Optionally there is a log of the UDP traffic for analysis. On Linux with systemd `journalctl -u meshcom-webdesk -f` shows the
running log.

## Troubleshooting

### No messages arrive

Look at the **UDP indicator** in the status bar and go through the list in the chapter
[Connecting the node](02-connect-node.md) (section "Checking that it works"). The most common causes:

- Target IP on the node wrong or a broadcast address (no longer allowed from firmware 4.40a.10)
- UDP port 1799 blocked in the firewall
- The **SSID** in the callsign does not match the node (warning **⚠️ Callsign?**)
- Node and computer not in the same network
- A second program occupies the port (🔴 *No socket*)

The button **🔍 Check settings** on the node card names concrete errors.

### I cannot reach WebDesk from my phone

- The computer and the phone must be in the **same network**, and the address is `http://<IP-of-the-computer>:5162`.
- Check whether a firewall blocks port **5162**.
- With access protection switched on you have to log in ([Security](09-security.md)).

### The app cannot be installed, desktop notifications do not appear

Both need a **secure connection** (HTTPS or `localhost`), see [Security](09-security.md). On a phone the certificate must also
be installed as trusted.

### A telemetry value does not reach the node

The node only takes over values if it has **no real sensors** and the firmware supports it. WebDesk suspends sending if no
matching echo comes from the node within 20 seconds. The setting is kept, though. More in [Automation](06-automation.md).

### Forgot the password

See [Security](09-security.md) (`--reset-password`).

### Something does not work and I do not know why

1. Open the **monitor** and check whether packets from the node arrive at all.
2. Look into the log file (see above).
3. If you report a problem on GitHub, include the **WebDesk version** (**About** page), the node's **firmware version** and your
   settings.

Continue with the [Reference](11-reference.md).
