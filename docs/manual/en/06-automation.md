# Automation

WebDesk can send messages by itself: beacons, event announcements, replies, bot commands and telemetry. Everything is under
**Settings → Sending & automation** and applies **immediately, without a restart**. What WebDesk replaces when sending
(e.g. `{mycall}`) is described in [Writing messages](04-writing-messages.md).

> ℹ️ All of these functions run through the **primary node**. A LoRa message may be at most 149 characters long; WebDesk splits
> longer bot replies and beacon texts into several packets.

## Beacon

The beacon regularly sends a fixed text to a group.

- You set the **text**, the **target group** and the **interval**. The interval is given in whole hours, **at least 8 hours**.
  WebDesk corrects values that are too small. The first transmission comes after **one full interval**, not at every restart.
- In the text you can use `{version}`, `{mycall}`, `{mylocator}`, `{date}`, `{time}` and `{telemetry}`.
- In the status bar a pulsing dot ● shows the next send time (yellow when it is less than 10 minutes away).
- The button **Send beacon now** sends immediately for testing.
- The beacon appears in the monitor and in the group's tab.

## Calendar beacon

The calendar beacon announces recurring events, for example the club meeting, in advance and at the time.

- You can create **any number of entries** and switch each on or off individually.
- **Recurrence:** once, weekly, every two weeks, monthly on a fixed day, on an nth weekday (e.g. 2nd Tuesday) or on the last
  weekday of the month.
- **Lead times** as a list, e.g. `3d, 24h, 2h` (`d` days, `h` hours, `m` minutes; without a unit hours apply). If you like, an
  announcement at the event time follows as well.
- In the text you can use `{title}`, `{event_date}`, `{event_time}`, `{days_until}` and `{hours_until}`, for example
  `📅 {title} on {event_date} at {event_time} – in {days_until} days!`
- The setting shows the **next calculated send time** as a preview.
- The text can also come from an **external script** (tick **⚙️ External**). The script receives the event data as JSON and
  outputs the text. If the output is empty, that announcement is skipped. The requirements are the same as for bot commands
  (see below).

## Auto-reply

The auto-reply answers automatically when **somebody writes to you for the first time** (a new direct tab is created).

- You write the reply text yourself. Variables such as `{mycall}`, `{callsign}`, `{mylocator}`, `{date}` or `{version}` are
  allowed.
- Example: `MeshCom WebDesk V{version} – QTH: {mylocator}`
- With the **test button** you send the text immediately to any callsign.

## Bot: commands by direct message

With the bot other stations can ask WebDesk something by **direct message**.

**How a command is recognised:** The message starts with `>`, `--` (two hyphens) or `—` (em dash, as many phone keyboards
type it) and **a letter right after it**: `>ping` and `--ping` are the same. This is because some devices, e.g. the LilyGo
T-Deck, have no `>` key. A plain `ping` without a character in front is ordinary chat text.

| Command | Reply |
|---|---|
| `>help` | List of all commands |
| `>version` | `MeshcomWebDesk vX.Y.Z` |
| `>time` | Current date and time |
| `>mh` | Number and callsigns of the stations heard recently |
| `>ping` | Reply with callsign, signal strength, path over relays and reception time |
| `>rx` | How the bot station receives **you**: direct or through a relay, with RSSI and SNR |
| `>echo <text>` | Sends the text back |

Even with the **bot switched off** (default) WebDesk answers `>ping`, `>rx`, `>version` and `>help`.

You create **your own commands** under **Settings → Sending & automation → Bot**: a name (without `>`), the reply text (with
variables) and a short description for `>help`. The **Test** button runs a command locally and shows the reply with the
variables replaced, without sending anything. With **Export** and **Import** you back up the commands in a file or share
them.

**External scripts ⚙️:** Instead of a fixed text a command can start a program or script (`.exe`, `.ps1`, `.bat`, `.cmd` or
`.py`). It receives sender, arguments, signal values and station data as **JSON on standard input** and writes the reply to
**standard output**. An empty output or an error means: no reply. The scripts live in the folder for external commands (with
Docker `./bot-scripts`); an example is `bot-command-example.py`. An adjustable time limit applies as well (default 10 seconds).

## Telemetry

With telemetry you send measured values (temperature, air pressure, humidity …) to a group or a callsign and/or to the node.

- **Source:** a JSON file or a program that sends the values to WebDesk with `POST /api/telemetry` (e.g. Home Assistant,
  Node-RED). Access can be protected with an API key (`X-Api-Key`).
- **Mapping:** In a table you assign each key a label, unit and number of decimals. A **role** (temperature, humidity,
  pressure …) decides where the value ends up in the map display and in the node telemetry.
- **Two ways:** as **chat text** (`TM: 🌡=10.7C 🧭=1022hPa …`) on a schedule and as **native node telemetry** (EXTUDP) when a
  value changes. Texts that are too long are split into `TM1:`, `TM2:` …
- The node only takes over the values if it has **no real sensors** built in and the firmware supports it. WebDesk checks
  this: if no matching echo arrives within 20 seconds, sending is only suspended at runtime and the setting is kept.
- A preview in the settings shows the current values and the finished message, and a button sends immediately for testing.

Guides: [Home Assistant](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-telemetry.md),
[practical setup](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/ext_udp_telemetry_guide.md) and
[protocol](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/ext_udp_telemetry.md).

## Weather data

Under **Settings → Integrations → Weather API** WebDesk fetches measurements from a weather platform (**AWEKAS**,
**Weather Underground** or a **simulation** for trying things out) and makes them available to the telemetry. The polling
interval is between 5 and 60 minutes. You enter the field names (e.g. `temp_out`, `humidity_out`, `pressure_rel`) in the
telemetry mapping. The bot command `--weather` reports the provider, the latest values and the time.

## Station and RF parameters

Under **Settings → Connection → Station / RF Parameters** you enter transmit power, cable, antenna, antenna height, frequency and
a system margin. From these WebDesk calculates the **EIRP** and the **theoretical free-space range**. The values are
available as variables (`{my-tx-power}`, `{my-eirp}`, `{my-antenna}`, `{my-freq}` …) in texts, and the range appears as a
circle on the [map](07-map.md).

Continue with [Map and range](07-map.md).
