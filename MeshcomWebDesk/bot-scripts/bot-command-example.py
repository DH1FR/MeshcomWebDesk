#!/usr/bin/env python3
"""
MeshCom WebDesk – Example External Bot Command
================================================
This script demonstrates how to implement an external bot command for
MeshCom WebDesk.  It is invoked by the WebDesk whenever a user sends
the configured command (e.g. --digi) to the bot.

HOW IT WORKS
  1. WebDesk starts this script (e.g. via Settings → Bot → External ⚙️)
  2. A JSON payload is written to stdin
  3. This script reads the payload, does its work, and prints a single
     reply line to stdout.
  4. Empty stdout  → no reply is sent to the MeshCom user.
  5. Exit code ≠ 0 → error is logged, no reply is sent.

PAYLOAD (stdin, UTF-8 JSON)
  {
    "command": "digi",
    "args":    ["arg1", "arg2"],        // everything after --digi
    "sender":  "OE5XYZ-7",             // callsign of the user who sent the command
    "message": {
      "rssi":      -87,
      "snr":       4.2,
      "route":     "OE1XAR-62,DB0TAW-13",
      "hops":      2,
      "srcType":   "lora",
      "timestamp": "2026-06-16T14:32:00",
      "lat":       48.2,
      "lon":       16.3,
      "alt":       220
    },
    "station": {
      "myCall":         "OE5ABC-1",
      "version":        "1.13.0",
      "txPowerDbm":     20,
      "frequencyMhz":   433.175,
      "antennaGainDbi": 2.15,
      "antennaType":    "Dipol",
      "antennaHeightM": 5.0
    }
  }

REPLY (stdout, UTF-8)
  A single text string.  The reply supports the same {variable} placeholders
  as static bot commands ({mycall}, {callsign}, {rssi}, {time}, …) – they
  are expanded by the WebDesk *after* this script exits.

CONFIGURE IN WEBDESK (Settings → 🤖 Bot)
  Name            : digi
  ⚙️ External     : ✓  (check the box)
  Filename        : bot-command-example.py
  Timeout (s)     : 10
  Description     : Digipeater info / example external command

  Set "External Commands Directory" to the folder containing this file.

CROSS-PLATFORM NOTES
  Windows : the WebDesk launches  python "path\bot-command-example.py"
  Linux   : the WebDesk launches  python "path/bot-command-example.py"
  Make sure `python` or `python3` is in PATH, or rename to point to the
  full interpreter path via a .bat / shell wrapper.
"""

import json
import sys
from datetime import datetime


def main() -> str | None:
    """
    Read the WebDesk JSON payload from stdin and return a reply string,
    or None to send no reply.
    """
    raw = sys.stdin.read()
    if not raw.strip():
        return None  # no payload → silent exit

    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as exc:
        print(f"[bot-command-example] JSON parse error: {exc}", file=sys.stderr)
        return None

    command = payload.get("command", "")
    args    = payload.get("args", [])
    sender  = payload.get("sender", "?")
    msg     = payload.get("message") or {}
    station = payload.get("station") or {}

    # ── Example: return different info depending on the first argument ───────

    sub = args[0].lower() if args else ""

    if sub == "info":
        rssi = msg.get("rssi")
        snr  = msg.get("snr")
        hops = msg.get("hops", 0)
        signal_part = f"RSSI {rssi} dBm, SNR {snr} dB" if rssi is not None else "no signal data"
        return (
            f"[{command}] Hallo {sender}! "
            f"{signal_part}, {hops} hop(s). "
            f"Station: {station.get('myCall', '?')} v{station.get('version', '?')}"
        )

    if sub == "time":
        now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        return f"[{command}] Server time: {now}"

    if sub == "echo":
        text = " ".join(args[1:]) if len(args) > 1 else "(nothing to echo)"
        return f"[{command}] Echo from {sender}: {text}"

    # ── Default: show usage hint ─────────────────────────────────────────────
    return (
        f"--{command} usage: "
        f"--{command} info | --{command} time | --{command} echo <text>"
    )


if __name__ == "__main__":
    reply = main()
    if reply:
        # Print without trailing newline so WebDesk gets exactly the reply text.
        print(reply, end="")
    sys.exit(0)
