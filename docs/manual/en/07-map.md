# Map and range

The **Map** page shows all stations whose position WebDesk knows on an **OpenStreetMap** map. It updates continuously as new
position packets arrive.

## Markers

- Each station is a **dot with its callsign**. The colour shows the signal strength: 🟢 better than −90 dBm, 🟡 better than
  −105 dBm, 🔴 weaker.
- **Gateways** appear on the map and in the MH list with a 🌐 symbol and their own colour. WebDesk fetches the list of
  gateways every 15 minutes from the MeshCom dashboards (OE, DL, IT, HB, SP). You switch this on and off and choose the source
  under **Settings → Chat & display → Chat & Monitor** (**Gateway Highlight**, **Gateway Source**).
- Your **own position** appears as a golden diamond ◆.
- A click on a marker opens a **window** with callsign, first name and place (if the QRZ.com integration is on), last
  message, signal strength, battery level, altitude, QTH locator, coordinates (link to OpenStreetMap) and a link to
  **aprs.fi**.
- If the node has sent telemetry, the window also shows temperature, air pressure and humidity.

## Searching and remembering

- The search field 🔍 jumps to a station and opens its window. With several hits a list appears, with none a message.
- On the **first visit** the map zooms to a radius of **50 km** around your station as soon as your position is known.
- Your browser remembers the map's position and zoom and restores them on your next visit.
- A compact line at the bottom shows the number of stations and your callsign.

## Range 📶

With the **📶** button in the map's bar you show the **range**:

- The **blue polygon** is the **measured range**. It encloses all stations heard directly (without a relay) and your own
  position.
- The **yellow circle** is the **theoretical free-space range**. It appears once you have entered power, antenna, frequency
  and system margin under **Station / RF Parameters** (see [Automation](06-automation.md)).
- The legend tells the two areas apart. The calculation runs anew each time you toggle the button.

## AI description of a station 🤖

If the AI integration is set up (see [Integrations](08-integrations.md)), each station's window has a 🤖 button. It sends
the station's data (callsign, signal, battery, firmware, path, QRZ data) to the AI and shows a short description right in
the window.

Continue with [Integrations](08-integrations.md).
