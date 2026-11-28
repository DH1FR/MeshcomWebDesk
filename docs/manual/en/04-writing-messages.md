# Writing messages

At the bottom of the chat is the **send bar**. You pick the recipient's tab at the top (callsign, group or **All**), type
the message and send it with **Send** or the Enter key.

## Message length

A LoRa message may be at most **149 characters** long. A counter **X/149** next to the input field counts along: grey,
yellow from 130 and red and bold from 145. The field does not accept more. Replies of the bot and beacon texts that are
longer are split into several packets by WebDesk itself (with a two-second pause in between).

## Variables: `{}`

In texts you can use **variables** in curly braces, which WebDesk replaces with the current value when sending. The
**`{}`** button in the send bar opens a scrollable table of all variables with a short description. A click inserts the
variable at the cursor position. Values that take a moment to determine are loaded only when you open the table.

You can also type variables directly, for example `{date}`. With the **Tab** key you see in advance what it becomes.
When sending, all variables are replaced in any case.

| Variable | Value |
|---|---|
| `{mycall}` | Own callsign |
| `{callsign}` | Callsign of the chat partner |
| `{dest-name}` / `{dest-loc}` | First name / location of the partner (from QRZ.com) |
| `{mylocator}` / `{locator}` | Own QTH locator / QTH locator of the partner |
| `{rssi}` / `{snr}` | Signal strength of the last reception |
| `{hw}` | Hardware type of the partner |
| `{route}` / `{hops}` | Relay path / number of relay hops |
| `{date}` / `{time}` | Current date / time |
| `{last-qso}` | Date and time of the **previous** contact with this callsign (`no QSO` if there was none) |
| `{telemetry}` | Telemetry text |
| `{node-firmware}` / `{node-hw}` | Firmware / hardware of your node |
| `{version}` | WebDesk version |
| `{my-tx-power}`, `{my-eirp}`, `{my-antenna}`, `{my-antenna-type}`, `{my-antenna-height}`, `{my-freq}` | Details of your station (from the settings) |

The same variables work in quick texts, auto-reply, bot commands and beacons. Which values WebDesk has depends on what
is known: without the QRZ.com integration, for example, there is no first name of the partner.

## Quick texts ⚡

Quick texts are buttons in the send bar with prepared texts. A click **loads the text into the input field**. You can
still adjust it before sending. Variables are allowed.

- You set up the buttons under **Settings → Quick Texts**. In the ⚡ menu you can reorder them by **drag & drop**; the
  new order is saved immediately.
- With **Export** and **Import** you back up the list in a file (`MeshComWebDesk-quick-texts.json`) or share it with other
  operators. An import replaces the existing list.

## Emoticons become emoji

WebDesk converts texts such as `;-)`, `:D` or `<3` into emoji: while you type, as soon as you press the space bar, and for
the last word when sending. This is on by default and can be switched off under
**Settings → Chat & display → Chat & Monitor** with **Convert emoticons to emoji**. It also works on the iPhone keyboard.

## Addressing others

- With the **@** button next to a callsign (in the chat or in the monitor) you insert `@CALLSIGN` at the cursor position.
  This is handy when you want to address somebody in a group.
- A click on a callsign opens that station's tab, so you do not have to search for it.
- With **+** you open a tab for a callsign or a group, e.g. `OE1KBC`, `#group` or `*`.

## Copying a message

If you hover over a message in a direct chat, a 📋 button appears. It copies the text to the clipboard.

## Spelling

The input field uses your **browser's spell checker**. Misspelled words are underlined if spell checking is switched on in
the browser settings.

More chapters will follow step by step. Until then the complete feature overview is in the
[README](https://github.com/DH1FR/MeshcomWebDesk#readme).
