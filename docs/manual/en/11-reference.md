# Reference

For looking things up: ports, files, commands and the layout of the settings.

## Ports

| Port | Protocol | Purpose |
|---|---|---|
| **5162** | TCP | WebDesk's interface (HTTP) |
| **5163** | TCP | Interface over HTTPS (only if set up) |
| **1799** | UDP | EXTUDP between node and WebDesk |
| **8001** | TCP | KISS/TCP on the node (only if switched on) |

## Starting with parameters

| Parameter | Effect |
|---|---|
| `--reset-password` | Switches access protection off |
| `--reset-password NEWPASSWORD` | Sets a new password |
| `MESHCOM_RESET_ACCESS=1` (environment variable) | Like `--reset-password`, for Docker without a shell |

Important values of the configuration file `appsettings.json` (or as the environment variable `Meshcom__<name>`, e.g.
`Meshcom__DeviceIp`):

| Name | Meaning |
|---|---|
| `DeviceIp` | IP address of the node |
| `MyCallsign` | Your callsign with SSID |
| `DataPath` | Folder for settings and data |
| `LogPath` | Folder for log files |

## Bot commands

`>` or `--` directly in front of the command, see [Automation](06-automation.md).

| Command | Reply |
|---|---|
| `>help` | List of all commands |
| `>version` | Version of WebDesk |
| `>time` | Date and time |
| `>mh` | Stations heard recently |
| `>ping` | Signal and path of the packet |
| `>rx` | How the bot station receives you |
| `>echo <text>` | Sends the text back |
| `--weather` | Weather data (only with the weather API set up) |

Plus the commands you create yourself.

## Variables

The complete table of variables (`{mycall}`, `{callsign}`, `{date}`, `{time}`, `{locator}`, `{last-qso}`, `{telemetry}`,
`{my-eirp}` …) is in [Writing messages](04-writing-messages.md). For auto-reply and bot there are in addition `{srctype}` and
`{srctype-label}`. The calendar beacon knows `{title}`, `{event_date}`, `{event_time}`, `{days_until}` and `{hours_until}`.

## Keys

| Key | Effect |
|---|---|
| **Enter** | Sends the message |
| **Tab** | Shows the replaced variables in the send bar |

## Meaning of the status displays

| Display | Meaning |
|---|---|
| 🔴 No socket | WebDesk cannot open the UDP port |
| 🟡 Waiting for signal | Port open, no packet from the node yet |
| 🟢 Receiving | Packets are arriving |
| ⚠️ Callsign? | The callsign (SSID) does not match the node |
| ● with time | Next beacon or telemetry |
| 🔔 / 🔕 | Sounds on / muted |
| 🔊 / 🔇 | Voice announcement on / off |

The delivery symbols (⏳ ✓ ✓✓ ☁️ ❌ 📬) are in [The interface](03-interface.md).

## Settings at a glance

The settings are arranged in five tabs. The search field at the top also finds a card through keywords.

| Tab | Cards |
|---|---|
| **🔌 Connection** | Nodes, KISS Hub, Station / RF Parameters, Console |
| **📡 Sending & automation** | Auto-Reply, Bot, Beacon, Calendar Beacon, Telemetry, Quick texts |
| **💬 Chat & display** | Chat & Monitor, Watchlist, Blacklist, Group labels, Language, Appearance, Web |
| **🔗 Integrations** | Weather API, MQTT, Webhook, QRZ.com, AI, Database |
| **🛠️ System** | Logging, Data Storage, Access from other devices, Access protection, Backup & Restore, License |

## Group labels and language

- Under **Group labels** you give display names to group numbers (e.g. `#262` → `DL`). The list is pre-filled with the official
  MeshCom groups and can be changed, exported and imported.
- The interface exists in **German, English, French, Italian and Spanish**. You choose the language under **Language**. It
  applies immediately. The manual is so far available in German and English.
- Your own translations or new languages go as a JSON file into `<DataPath>/languages/` (guide:
  [translations.md](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/translations.md)).

## Further guides

- [Node connection](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/node-connection-en.md) and
  [KISS/TCP](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/kiss-tcp-guide-en.md)
- [Docker update](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/docker-update-en.md)
- [Telemetry with Home Assistant](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-telemetry.md) and
  [unread alert](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-unread-alert.md)
- [README with all features](https://github.com/DH1FR/MeshcomWebDesk#readme) and
  [what is new per version](https://github.com/DH1FR/MeshcomWebDesk/releases)

Back to [Welcome](00-welcome.md).
