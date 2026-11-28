# Notifications

WebDesk alerts you in several ways when something important happens. Almost everything can be switched on and off
individually.

## Sound and voice

- A new **direct message to your callsign** plays an alert sound. The **🔔 / 🔕** switch in the status bar mutes all
  sounds.
- If you wish, the browser **reads direct messages aloud** (the browser's speech output, no extra file needed). You find
  the **🔊 / 🔇** switch in the status bar as well. The language of the announcement follows the interface language.

Your browser remembers both switches.

## Watchlist: watching callsigns

Under **Settings → Chat & display → Watchlist** you enter callsigns you want to keep an eye on. WebDesk alerts you as soon
as one of them is heard.

- **Without SSID** (e.g. `DH1FR`) matches all SSIDs (`DH1FR`, `DH1FR-1`, `DH1FR-11` …). **With SSID** (e.g. `DH1FR-1`)
  matches exactly that callsign only.
- On a hit you hear a distinct three-tone beep, and a **notice** (toast) appears at the top right with the callsign, the
  packet type (`MSG`, `POS`, `TEL`, `ACK`) and its age. Several hits are shown in the same notice. It disappears after an
  adjustable time (default 5 minutes) or with ✕.
- You choose **which packets** raise an alert: messages and positions are usually useful; telemetry and acknowledgements are
  off by default because they arrive often and regularly.
- The **group list** (e.g. `26261`) reports every new text message in that group with a 👥 notice, the tone and an
  announcement. Your own messages do not count, and the announcement comes at most every 5 minutes per group (otherwise
  just the tone).
- Changes apply immediately, without a restart.

## CQ calls

WebDesk detects **CQ calls** in groups and also **CQ to everybody** (it lands in the **All** tab). A yellow notice **CQ**
appears with the callsign and group (visible for 60 seconds by default), plus a CQ tone in Morse code and, if voice output
is on, an announcement such as "C Q C Q from … in group 262". Your own calls and words like "FREQUENCY" trigger nothing. The
switch **CQ calls to everybody** is in the same Watchlist card (on by default).

## Mentions with `@`

If somebody writes you a message in a **group** or to **everybody** containing `@<your callsign>` (e.g. `@DH1FR-4`), WebDesk
recognises it:

- The row gets a strong tint with a coloured edge and the tag **@ for you**. The callsign in the text is highlighted, and the
  tab gets an **@N** counter.
- In addition you get a notice **You were mentioned** (a click opens the tab), its own tone and an announcement, at most once
  a minute per tab.
- The whole callsign counts, ignoring upper/lower case: `@DH1FR-4` is for your `DH1FR-4`, `@DH1FR` without SSID for every
  callsign with that base. Other SSIDs, e-mail addresses and foreign callsigns do not count.
- You switch the alert tone under **Watchlist → Mention** (on by default). The row mark and the tab counter always stay.

Your own callsign is highlighted in every message, old ones included.

## Unread count in the browser tab

While messages are unread, the browser tab's title shows the number, e.g. `(3) MeshCom WebDesk`, and the icon gets a red
dot. On an installed app the number is also shown on the icon (taskbar or dock). This works when the sound is off. Direct
messages, groups and **All** of all nodes are counted. The switch is called **Show unread count in browser tab**
(**Settings → Chat & display**).

## Desktop notifications

When nobody is looking at WebDesk, it can send operating-system notifications (pop-ups) for direct messages, mentions,
watchlist callsigns, watched groups and CQ calls. While you are looking at WebDesk there is no pop-up, so nothing is
reported twice. A click brings the window to the front and opens the matching tab.

This is **off by default**. You switch it on under **Watchlist → Desktop notifications**; the browser asks for permission
once. There you also choose which kinds you want and can try it with a button. A secure connection (HTTPS or `localhost`)
is required. A phone with a locked screen or a closed app receives nothing – for that, see the option below.

## Phone notification for unseen messages (MQTT)

If you have not seen a direct message yourself, WebDesk can report this over **MQTT**, for example for a notification on
your phone in Home Assistant. How to set it up is covered in [Integrations](08-integrations.md) and in the
[Home Assistant unread alert guide](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-unread-alert.md).

Continue with [Automation](06-automation.md).
