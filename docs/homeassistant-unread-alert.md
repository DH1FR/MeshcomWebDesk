# Home Assistant – Unread Direct Message Alert

This guide explains the **MQTT unread alert** of MeshCom WebDesk and how to use it in **Home Assistant** (or any other MQTT consumer such as Node-RED) to get a phone notification for direct messages you have not seen yourself.

## Why this exists

WebDesk already published every incoming message to MQTT (`{prefix}/dm/{callsign}`). That is fine for logging, but useless as a *notification* trigger: if WebDesk is open on your desktop and you are sitting in front of it, you do not want your phone to buzz for every direct message – you only want to be told about messages that **nobody has looked at**.

WebDesk cannot know that from the message alone, so it now tracks it:

- Only **direct messages addressed to your own callsign** are considered (the callsign of the respective node). Messages to groups, broadcasts and messages from other stations heard on the mesh that are addressed to someone else never raise an alert.
- Such a direct message counts as **seen** only while a viewer is *really looking*: the chat page is open in a **visible** browser tab, that conversation is the active tab, and there was **mouse / keyboard / touch activity** within the last 30 s. An open but idle, minimised or forgotten browser does not count.
- If nobody has looked at the conversation within the **wait time** (default 60 s), WebDesk publishes `{prefix}/unread/{callsign}`.
- Once someone looks at that conversation afterwards, WebDesk publishes `{prefix}/read/{callsign}`, so the notification can be removed again (e.g. when you pick up the message on the PC after all).

Bot commands sent to you never raise an alert.

## Topics

| Topic | When |
|-------|------|
| `{prefix}/unread/{callsign}` | A direct message from `{callsign}` to your own callsign stayed unseen for the wait time. Several messages arriving meanwhile are reported together (`count`). |
| `{prefix}/read/{callsign}` | The conversation was looked at after an `unread` alert was sent. |

`{prefix}` is the MQTT topic prefix (default `meshcom`), `{callsign}` is the **sender** of the message to you (e.g. `DL2BBB-2`); your own callsign is in the payload field `to`. Messages are not retained.

Payload of `unread`:

```json
{
  "event": "unread",
  "timestamp": "2026-10-14T18:31:02",
  "from": "DL2BBB-2",
  "to": "DH1FR-2",
  "count": 2,
  "text": "Are you there?",
  "message_timestamp": "2026-10-14T18:30:55",
  "rssi": -95,
  "snr": 6.5,
  "relay_path": "DB0KH-11",
  "src_type": "lora"
}
```

Payload of `read`: `{"event":"read","timestamp":"…","from":"DL2BBB-2","to":"DH1FR-2"}`

## Settings

In WebDesk under **Settings → MQTT → Unread alert** (MQTT itself must be enabled):

| Setting | Default | Meaning |
|---------|---------|---------|
| Unread alert (`UnreadAlertEnabled`) | on | Publish `unread` / `read` topics |
| Wait time (`UnreadAlertSeconds`) | 60 | How long a direct message may stay unseen before the alert (5–3600 s) |
| Activity window (`UnreadActiveSeconds`) | 30 | A visible tab with input activity within this time counts as "looking" (5–3600 s) |

## Home Assistant automation

Requires the MQTT integration of Home Assistant connected to the same broker as WebDesk.

```yaml
automation:
  - alias: "MeshCom: unread direct message"
    trigger:
      - platform: mqtt
        topic: "meshcom/unread/+"
    action:
      - service: notify.mobile_app_my_phone
        data:
          title: "MeshCom: {{ trigger.payload_json.from }}"
          message: >
            {{ trigger.payload_json.text | default('(no text)') }}
            {% if trigger.payload_json.count > 1 %}({{ trigger.payload_json.count }} messages){% endif %}
          data:
            tag: "meshcom-{{ trigger.payload_json.from }}"

  - alias: "MeshCom: dismiss notification once read"
    trigger:
      - platform: mqtt
        topic: "meshcom/read/+"
    action:
      - service: notify.mobile_app_my_phone
        data:
          message: clear_notification
          data:
            tag: "meshcom-{{ trigger.payload_json.from }}"
```

Adjust `notify.mobile_app_my_phone` to your device. The `tag` makes a newer alert from the same station replace the older notification and lets the `read` automation remove exactly that one.

## Per-node unread badge

Independent of MQTT, the node switcher in WebDesk now shows a small badge with the number of **unread direct messages per node** (groups and broadcasts are not counted), so in a multi-node setup you can see at a glance which node has mail waiting. The same "really looking" rule applies: a conversation is only marked as read while somebody is actually looking at it.
