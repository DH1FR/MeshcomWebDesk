#!/usr/bin/env python3
"""
MeshCom WebDesk – External Bot Command: --prop
===============================================
Returns current solar propagation conditions from HamQSL.com.

Example reply:
  SFI=145 SN=83 A=8 K=1 X=A1.4 Quiet

Uses only Python stdlib (urllib, xml) – no pip install needed.

CONFIGURE IN WEBDESK (Settings → 🤖 Bot)
  Name            : prop
  ⚙️ External     : ✓  (check the box)
  Filename        : prop.py
  Timeout (s)     : 15
  Description     : Solar propagation conditions (SFI, K/A-index, sunspots)

  Set "External Commands Directory" to the folder containing this file.
"""

import json
import sys
import urllib.request
import xml.etree.ElementTree as ET

HAMQSL_URL = "https://www.hamqsl.com/solarxml.php"
TIMEOUT_S  = 10


def fetch_propagation() -> str:
    req = urllib.request.Request(
        HAMQSL_URL,
        headers={"User-Agent": "MeshCom-WebDesk/1.0 (prop.py; +hamqsl)"},
    )
    with urllib.request.urlopen(req, timeout=TIMEOUT_S) as resp:
        xml_bytes = resp.read()

    root = ET.fromstring(xml_bytes)
    # Structure: <solarxml><solardata>…</solardata></solarxml>
    sd_el = root.find("solardata")
    sd = sd_el if sd_el is not None else root

    def get(tag: str) -> str | None:
        el = sd.find(tag)
        return el.text.strip() if el is not None and el.text else None

    parts = []
    if (v := get("solarflux"))  is not None: parts.append(f"SFI={v}")
    if (v := get("sunspots"))   is not None: parts.append(f"SN={v}")
    if (v := get("aindex"))     is not None: parts.append(f"A={v}")
    if (v := get("kindex"))     is not None: parts.append(f"K={v}")
    if (v := get("xray"))       is not None: parts.append(f"X={v}")
    if (v := get("geomagfield"))is not None: parts.append(v)

    return " ".join(parts) if parts else "Prop: no data"


def main() -> str | None:
    # Read and discard the JSON payload (--prop needs no arguments)
    raw = sys.stdin.read()
    try:
        payload = json.loads(raw) if raw.strip() else {}
    except json.JSONDecodeError:
        payload = {}

    _ = payload  # unused but available for future sub-commands

    try:
        return fetch_propagation()
    except Exception as exc:
        print(f"[prop] error: {exc}", file=sys.stderr)
        return f"Prop: {exc}"


if __name__ == "__main__":
    reply = main()
    if reply:
        print(reply, end="")
    sys.exit(0)
