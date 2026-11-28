# Integrations

Under **Settings → Integrations** you connect WebDesk with other services. Everything is **optional** and off by default.
Changes apply immediately, without a restart.

> 🔒 WebDesk stores passwords and keys (QRZ.com, MQTT, AI, database) **encrypted** in the settings.

## QRZ.com: name and place

With a free [QRZ.com](https://www.qrz.com/register) account (user name and password, no separate key) WebDesk fetches the
**first name and home location** of radio amateurs.

- Looked up without the SSID: `DH1FR-2` is looked up as `DH1FR`. Each callsign is queried only once per session.
- The data appears in the **MH list** (Name / Location column), as a **hint** over callsigns in the chat and in the monitor and
  in the **window on the map**. It is also available as `{dest-name}` and `{dest-loc}` for texts.
- A test button looks up your own callsign, another clears the cache.
- Errors (wrong password, network error, unknown callsign) end up as a warning in the log file.

## Database

WebDesk can store all monitor data permanently in a database:

- **MySQL / MariaDB** (one row per monitor entry)
- **InfluxDB 2** (one data point per monitor entry)

With **Test Connection** WebDesk detects a missing database, table or bucket and creates it with one click if you want. If
you like, every successful write is noted in the log (a privacy notice is shown in the settings).

With **MySQL** the **history** and the **search** are available as well: every direct chat then has a 🔎 button with the
**QSO dialog** (history with filter, text search) and a 📋 button for the partners contacted most recently.

## AI

With an AI account (e.g. OpenAI or Azure OpenAI) and a MySQL database the QSO dialog gets two more tabs:

| Tab | Function |
|---|---|
| **AI summary** | Summarises the latest messages of a contact |
| **AI search** | Answers a question in your own words and names the times of the findings |

Without an AI account these two tabs are greyed out; history and search still work. The [map](07-map.md) also uses the AI
for the description of a station.

## Webhook

The webhook sends events as an **HTTP POST** (JSON) to an address of your choice, for example to Home Assistant, Node-RED,
n8n or IFTTT.

- You choose which events are sent: **messages**, **positions** and **telemetry** separately.
- The JSON contains among other things `event`, `timestamp`, `from`, `to`, `text`, `rssi`, `snr`, position, battery,
  firmware and the relay path. Empty fields are left out.
- The call waits at most 10 seconds and never disturbs reception. Errors are only logged.

## MQTT

With **MQTT** WebDesk connects to a broker (e.g. Mosquitto or the Mosquitto add-on of Home Assistant). You can enter a user
name and password, **TLS** and a **topic prefix** (default `meshcom`). If the connection is lost, WebDesk reconnects by
itself.

**Publishing:** WebDesk sends incoming events to these topics (the content is the same JSON as for the webhook):

| Event | Topic |
|---|---|
| Broadcast | `{prefix}/broadcast` |
| Group | `{prefix}/group/{group}` |
| Direct message | `{prefix}/dm/{callsign}` |
| Position | `{prefix}/position/{callsign}` |
| Telemetry | `{prefix}/telemetry/{callsign}` |
| Unread alert | `{prefix}/unread/{callsign}` |
| Read | `{prefix}/read/{callsign}` |

**Unread alert:** It is meant for a phone notification that only arrives when you have **not seen** a direct message
**yourself**. When a direct message arrives (bot commands do not count) and nobody has looked at the chat within the
**wait time** (default 60 seconds), WebDesk publishes `unread` with the number of unseen messages and the latest text.
"Looked at" means: the chat is open in a **visible** browser tab, the conversation is the active tab, and there was mouse,
keyboard or touch activity in the last 30 seconds (adjustable). As soon as you look at the conversation later, `read` is
published so the notification disappears again. A ready-made Home Assistant automation is in the
[guide](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-unread-alert.md).

**Sending (optional):** WebDesk can subscribe to topics and send their content as a message:

| Topic | Effect |
|---|---|
| `{prefix}/send/broadcast` | Sends to everybody |
| `{prefix}/send/group/{group}` | Sends to a group |
| `{prefix}/send/dm/{callsign}` | Sends a direct message |

The content is JSON, e.g. `{ "text": "message" }`. Variables such as `{mycall}` or `{date}` are replaced before sending.

## Other ways

- **Telemetry** from outside into WebDesk: `POST /api/telemetry`, see [Automation](06-automation.md).
- **Weather data** (AWEKAS, Weather Underground): also under **Integrations**, see [Automation](06-automation.md).

More chapters will follow. Until then you find security, backup and update in the chapter
[Quick start and installation](01-install.md) and in the [README](https://github.com/DH1FR/MeshcomWebDesk#readme).
