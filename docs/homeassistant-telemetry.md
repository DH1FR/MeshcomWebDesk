# Home Assistant – Telemetry Integration

This guide explains how to send weather data (or any other sensor data) from **Home Assistant** as telemetry messages via MeshCom WebDesk into the LoRa network.

## How it works

```
Home Assistant                WebDesk                     MeshCom Node
---------------------        ------------------          -------------
Sensor data               →  reads JSON file          →  sends text message
writes / posts data          every X hours               into the LoRa network
```

---

## Option A – HTTP POST *(recommended for separate hosts)*

Home Assistant sends data directly to WebDesk via HTTP POST.
No network shares or SSH configuration required.

### Step 1 – Enable WebDesk settings

Enable the following in the WebDesk settings (`/settings`, section **Telemetry**):

| Setting | Value |
|---------|-------|
| Telemetry active | ✔ |
| JSON file | `/app/data/meshcom_telemetry.json` |
| Target | `#262` (group), `*` (all) or a callsign |
| HTTP API active | ✔ |
| API key | e.g. `my-secret-key` (empty = no authentication) |

> 💡 **Tip:** Use the **▶ Send telemetry now** button to trigger a test transmission immediately after saving, without waiting an hour.
> Use **📄 Read file & preview** to see the current values from the JSON file and the exact LoRa messages.

### Step 2 – `configuration.yaml` in Home Assistant

```yaml
rest_command:
  post_meshcom_telemetry:
    url: "http://192.168.1.100:5162/api/telemetry"   # adjust to your WebDesk IP
    method: POST
    headers:
      Content-Type: application/json
      X-Api-Key: "my-secret-key"                      # must match the API key in WebDesk
    payload: >-
      {
        "timestamp":    "{{ now().isoformat() }}",
        "outside_temp": {{ states('sensor.tempoutside_2')                              | float(0) }},
        "pressure":     {{ states('sensor.weatherstation_rel_pressure')                | float(0) }},
        "humidity":     {{ states('sensor.weatherstation_rel_humidity_outside')        | float(0) }},
        "wind_speed":   {{ states('sensor.weatherstation_wind_speed')                  | float(0) }},
        "wind_gust":    {{ states('sensor.weatherstation_wind_gust_2')                 | float(0) }},
        "wind_dir":     {{ states('sensor.weatherstation_wind_dir')                    | float(0) }},
        "rain_24h":     {{ states('sensor.weatherstation_rain_24h')                    | float(0) }},
        "rain_total":   {{ states('sensor.weatherstation_rain_total')                  | float(0) }}
      }
```

### Step 3 – Automation in Home Assistant

```yaml
- alias: "MeshCom Telemetry send (HTTP POST)"
  description: "Send weather data every hour via HTTP POST to MeshCom WebDesk"
  trigger:
    - platform: time_pattern
      minutes: "0"
  action:
    - service: rest_command.post_meshcom_telemetry
  mode: single
```

### Endpoint response

| HTTP status | Meaning |
|-------------|---------|
| `200 OK` | File written; telemetry will be sent at the next scheduled interval |
| `401 Unauthorized` | API key missing or incorrect |
| `404 Not Found` | Endpoint is disabled in WebDesk settings |
| `400 Bad Request` | Body is not valid JSON, or `TelemetryFilePath` is not configured |

Example response on success:
```json
{ "written": "/app/data/meshcom_telemetry.json", "timestamp": "2026-04-04T10:00:00Z" }
```

---

## Option B – JSON file via Shell Command *(same host)*

If Home Assistant and WebDesk Docker run on the **same host**, HA can write directly
into the mounted data volume.

### Step 1 – Shell Command in Home Assistant

Add the following block to your `configuration.yaml`:

```yaml
shell_command:
  export_meshcom_telemetry: >-
    python3 -c "
    import json, datetime;
    data = {
      'timestamp':    datetime.datetime.now(datetime.timezone.utc).isoformat(),
      'outside_temp': {{ states('sensor.tempoutside_2')                              | float(0) }},
      'pressure':     {{ states('sensor.weatherstation_rel_pressure')                | float(0) }},
      'humidity':     {{ states('sensor.weatherstation_rel_humidity_outside')        | float(0) }},
      'wind_speed':   {{ states('sensor.weatherstation_wind_speed')                  | float(0) }},
      'wind_gust':    {{ states('sensor.weatherstation_wind_gust_2')                 | float(0) }},
      'wind_dir':     {{ states('sensor.weatherstation_wind_dir')                    | float(0) }},
      'rain_24h':     {{ states('sensor.weatherstation_rain_24h')                    | float(0) }},
      'rain_total':   {{ states('sensor.weatherstation_rain_total')                  | float(0) }}
    };
    open('/opt/meshcom/data/meshcom_telemetry.json', 'w').write(json.dumps(data, indent=2))
    "
```

> **Path note:** `/opt/meshcom/data/` is the host-mounted `./data` volume of WebDesk
> (`./data:/app/data` in `docker-compose.yml`). Adjust the path to match your host setup.

### Step 2 – Automation

```yaml
- alias: "MeshCom Telemetry export (file)"
  description: "Write weather data every hour as JSON file for MeshCom WebDesk"
  trigger:
    - platform: time_pattern
      minutes: "0"
  action:
    - service: shell_command.export_meshcom_telemetry
  mode: single
```

---

## Generated JSON file (example)

```json
{
  "timestamp":    "2026-04-04T10:00:00+00:00",
  "outside_temp": 10.7,
  "pressure":     1022.3,
  "humidity":     86.0,
  "wind_speed":   0.0,
  "wind_gust":    0.0,
  "wind_dir":     180.0,
  "rain_24h":     0.9,
  "rain_total":   0.0
}
```

The file can contain **any number** of values. In WebDesk you configure which ones (maximum 3) are sent.

---

## WebDesk Settings – TelemetryMapping

Since MeshCom supports a maximum of **5 telemetry values** per message, select the values most relevant to you.

### Variant A – Temperature, Pressure, Humidity, Wind

```json
"TelemetryEnabled":       true,
"TelemetryFilePath":      "/app/data/meshcom_telemetry.json",
"TelemetryGroup":         "#262",
"TelemetryScheduleHours": "11,15",
"TelemetryApiEnabled":    true,
"TelemetryApiKey":        "my-secret-key",
"TelemetryMapping": [
  { "JsonKey": "outside_temp", "Label": "temp.out", "Unit": "C",   "Decimals": 1 },
  { "JsonKey": "pressure",     "Label": "pressure", "Unit": "hPa", "Decimals": 1 },
  { "JsonKey": "humidity",     "Label": "humid",    "Unit": "%",   "Decimals": 0 },
  { "JsonKey": "wind_speed",   "Label": "wind",     "Unit": "m/s", "Decimals": 1 },
  { "JsonKey": "wind_gust",    "Label": "gust",     "Unit": "m/s", "Decimals": 1 }
]
```

**Sent message:** `TM: temp.out=10.7C pressure=1022.3hPa humid=86% wind=0.0m/s gust=0.0m/s`

---

### Variant B – Temperature, Pressure, Humidity, Rain, Wind direction

```json
"TelemetryMapping": [
  { "JsonKey": "outside_temp", "Label": "temp.out",  "Unit": "C",    "Decimals": 1 },
  { "JsonKey": "pressure",     "Label": "pressure",  "Unit": "hPa",  "Decimals": 1 },
  { "JsonKey": "humidity",     "Label": "humid",     "Unit": "%",    "Decimals": 0 },
  { "JsonKey": "rain_24h",     "Label": "rain.24h",  "Unit": "l/m2", "Decimals": 1 },
  { "JsonKey": "wind_dir",     "Label": "wind.dir",  "Unit": "deg",  "Decimals": 0 }
]
```

**Sent message:** `TM: temp.out=10.7C pressure=1022.3hPa humid=86% rain.24h=0.9l/m2 wind.dir=180deg`

---

## Sensor reference (used Entity IDs)

| JSON key | HA Entity ID | Unit | Description |
|----------|-------------|------|-------------|
| `outside_temp` | `sensor.tempoutside_2` | °C | Outside temperature |
| `pressure` | `sensor.weatherstation_rel_pressure` | hPa | Relative air pressure |
| `humidity` | `sensor.weatherstation_rel_humidity_outside` | % | Outside humidity |
| `wind_speed` | `sensor.weatherstation_wind_speed` | m/s | Wind speed |
| `wind_gust` | `sensor.weatherstation_wind_gust_2` | m/s | Wind gust |
| `wind_dir` | `sensor.weatherstation_wind_dir` | ° | Wind direction |
| `rain_24h` | `sensor.weatherstation_rain_24h` | l/m² | Rain last 24 h |
| `rain_total` | `sensor.weatherstation_rain_total` | l/m² | Total rain |

---

## Adding more sensors

The JSON file can contain any number of values. You do **not need to change any WebDesk code** –
simply add new fields and configure the mapping in the WebDesk settings under `/settings`.

Example – PV system added to `rest_command` or `shell_command`:

```yaml
"pv_power": {{ states('sensor.pv_power') | float(0) }},
"batt_soc":  {{ states('sensor.battery_soc') | float(0) }}
```

```json
{ "JsonKey": "pv_power", "Label": "PV", "Unit": "kW", "Decimals": 2 }
```