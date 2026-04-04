# Home Assistant – Telemetrie-Integration

Diese Anleitung zeigt, wie du Wetterdaten (oder beliebige andere Sensordaten) aus **Home Assistant** als Telemetrienachrichten über den MeshCom WebClient ins LoRa-Netzwerk sendest.

## Funktionsweise

```
Home Assistant                WebClient                   MeshCom Node
─────────────────────        ──────────────────          ─────────────
Sensordaten               →  liest JSON-Datei         →  sendet Textnachricht
schreibt/postet Daten        alle X Stunden              ins LoRa-Netz
```

---

## Variante A – HTTP POST *(empfohlen bei getrennten Hosts)*

Home Assistant sendet die Daten per HTTP POST direkt an den WebClient.
Keine Netzwerkfreigaben oder SSH-Konfiguration erforderlich.

### Schritt 1 – WebClient Settings aktivieren

Aktiviere in den WebClient-Einstellungen (`/settings`, Abschnitt **Telemetrie**):

| Einstellung | Wert |
|-------------|------|
| Telemetrie aktiv | ✅ |
| JSON-Datei | `/app/data/meshcom_telemetry.json` |
| HTTP-API aktiv | ✅ |
| API-Key | z.B. `mein-geheimer-schluessel` (leer = keine Authentifizierung) |

### Schritt 2 – `configuration.yaml` in Home Assistant

```yaml
rest_command:
  post_meshcom_telemetry:
    url: "http://192.168.1.100:5162/api/telemetry"   # WebClient-IP anpassen
    method: POST
    headers:
      Content-Type: application/json
      X-Api-Key: "mein-geheimer-schluessel"           # muss mit API-Key im WebClient übereinstimmen
    payload: >-
      {
        "timestamp": "{{ now().isoformat() }}",
        "aussentemp":        {{ states('sensor.tempoutside_2')                              | float(0) }},
        "luftdruck":         {{ states('sensor.weatherstation_rel_pressure')                | float(0) }},
        "luftfeuchtigkeit":  {{ states('sensor.weatherstation_rel_humidity_outside')        | float(0) }},
        "wind_speed":        {{ states('sensor.weatherstation_wind_speed')                  | float(0) }},
        "wind_gust":         {{ states('sensor.weatherstation_wind_gust_2')                 | float(0) }},
        "wind_dir":          {{ states('sensor.weatherstation_wind_dir')                    | float(0) }},
        "regen_24h":         {{ states('sensor.weatherstation_rain_24h')                    | float(0) }},
        "regen_gesamt":      {{ states('sensor.weatherstation_rain_total')                  | float(0) }}
      }
```

### Schritt 3 – Automatisierung in Home Assistant

```yaml
- alias: "MeshCom Telemetrie senden (HTTP POST)"
  description: "Wetterdaten jede Stunde per HTTP POST an MeshCom WebClient senden"
  trigger:
    - platform: time_pattern
      minutes: "0"
  action:
    - service: rest_command.post_meshcom_telemetry
  mode: single
```

### Antwort des Endpoints

| HTTP-Status | Bedeutung |
|-------------|-----------|
| `200 OK` | Datei geschrieben, Telemetrie wird beim nächsten Intervall gesendet |
| `401 Unauthorized` | API-Key fehlt oder stimmt nicht überein |
| `404 Not Found` | Endpoint ist in den WebClient-Settings deaktiviert |
| `400 Bad Request` | Body ist kein gültiges JSON oder `TelemetryFilePath` nicht konfiguriert |

Beispiel-Antwort bei Erfolg:
```json
{ "written": "/app/data/meshcom_telemetry.json", "timestamp": "2026-04-04T10:00:00Z" }
```

---

## Variante B – JSON-Datei per Shell Command *(gleicher Host)*

Wenn HA und WebClient-Docker auf **demselben Host** laufen, kann HA
direkt in das gemountete Data-Volume schreiben.

### Schritt 1 – Shell Command in Home Assistant

Füge folgenden Block in deine `configuration.yaml` ein:

```yaml
shell_command:
  export_meshcom_telemetry: >-
    python3 -c "
    import json, datetime;
    data = {
      'timestamp':        datetime.datetime.now(datetime.timezone.utc).isoformat(),
      'aussentemp':        {{ states('sensor.tempoutside_2')                              | float(0) }},
      'luftdruck':         {{ states('sensor.weatherstation_rel_pressure')                | float(0) }},
      'luftfeuchtigkeit':  {{ states('sensor.weatherstation_rel_humidity_outside')        | float(0) }},
      'wind_speed':        {{ states('sensor.weatherstation_wind_speed')                  | float(0) }},
      'wind_gust':         {{ states('sensor.weatherstation_wind_gust_2')                 | float(0) }},
      'wind_dir':          {{ states('sensor.weatherstation_wind_dir')                    | float(0) }},
      'regen_24h':         {{ states('sensor.weatherstation_rain_24h')                    | float(0) }},
      'regen_gesamt':      {{ states('sensor.weatherstation_rain_total')                  | float(0) }}
    };
    open('/opt/meshcom/data/meshcom_telemetry.json', 'w').write(json.dumps(data, indent=2))
    "
```

> **Pfadhinweis:** `/opt/meshcom/data/` ist das auf dem Docker-Host gemountete `./data`-Volume
> des WebClients (`./data:/app/data` in `docker-compose.yml`). Passe den Pfad an deinen Host an.

### Schritt 2 – Automatisierung

```yaml
- alias: "MeshCom Telemetrie exportieren (Datei)"
  description: "Wetterdaten jede Stunde als JSON-Datei für MeshCom WebClient schreiben"
  trigger:
    - platform: time_pattern
      minutes: "0"
  action:
    - service: shell_command.export_meshcom_telemetry
  mode: single
```

---

## Erzeugte JSON-Datei (Beispiel)

```json
{
  "timestamp": "2026-04-04T10:00:00+00:00",
  "aussentemp":       10.7,
  "luftdruck":        1022.3,
  "luftfeuchtigkeit": 86.0,
  "wind_speed":       0.0,
  "wind_gust":        0.0,
  "wind_dir":         180.0,
  "regen_24h":        0.9,
  "regen_gesamt":     0.0
}
```

Die Datei enthält **alle** Messwerte. Im WebClient konfigurierst du, welche davon
(maximal 5) gesendet werden.

---

## WebClient Settings – TelemetryMapping

Da MeshCom maximal **5 Telemetriewerte** pro Nachricht unterstützt, wähle die für dich
relevanten Werte aus. Zwei fertige Varianten:

### Variante A – Temperatur, Druck, Feuchte, Wind

```json
"TelemetryEnabled":       true,
"TelemetryFilePath":      "/app/data/meshcom_telemetry.json",
"TelemetryGroup":         "#262",
"TelemetryIntervalHours": 1,
"TelemetryApiEnabled":    true,
"TelemetryApiKey":        "mein-geheimer-schluessel",
"TelemetryMapping": [
  { "JsonKey": "aussentemp",       "Label": "temp.out", "Unit": "C",   "Decimals": 1 },
  { "JsonKey": "luftdruck",        "Label": "luftdr",   "Unit": "hPa", "Decimals": 1 },
  { "JsonKey": "luftfeuchtigkeit", "Label": "humid",    "Unit": "%",   "Decimals": 0 },
  { "JsonKey": "wind_speed",       "Label": "wind",     "Unit": "m/s", "Decimals": 1 },
  { "JsonKey": "wind_gust",        "Label": "boe",      "Unit": "m/s", "Decimals": 1 }
]
```

**Gesendete Nachricht:** `TM: temp.out=10.7C luftdr=1022.3hPa humid=86% wind=0.0m/s boe=0.0m/s`

---

### Variante B – Temperatur, Druck, Feuchte, Regen, Windrichtung

```json
"TelemetryMapping": [
  { "JsonKey": "aussentemp",       "Label": "temp.out",  "Unit": "C",    "Decimals": 1 },
  { "JsonKey": "luftdruck",        "Label": "luftdr",    "Unit": "hPa",  "Decimals": 1 },
  { "JsonKey": "luftfeuchtigkeit", "Label": "humid",     "Unit": "%",    "Decimals": 0 },
  { "JsonKey": "regen_24h",        "Label": "rain.24h",  "Unit": "l/m2", "Decimals": 1 },
  { "JsonKey": "wind_dir",         "Label": "wind.dir",  "Unit": "°",    "Decimals": 0 }
]
```

**Gesendete Nachricht:** `TM: temp.out=10.7C luftdr=1022.3hPa humid=86% rain.24h=0.9l/m2 wind.dir=180°`

---

## Sensor-Referenz (verwendete Entity-IDs)

| JSON-Key | HA Entity-ID | Einheit | Beschreibung |
|----------|-------------|---------|-------------|
| `aussentemp` | `sensor.tempoutside_2` | °C | Außentemperatur |
| `luftdruck` | `sensor.weatherstation_rel_pressure` | hPa | Relativer Luftdruck |
| `luftfeuchtigkeit` | `sensor.weatherstation_rel_humidity_outside` | % | Außenluftfeuchtigkeit |
| `wind_speed` | `sensor.weatherstation_wind_speed` | m/s | Windgeschwindigkeit |
| `wind_gust` | `sensor.weatherstation_wind_gust_2` | m/s | Windböen |
| `wind_dir` | `sensor.weatherstation_wind_dir` | ° | Windrichtung |
| `regen_24h` | `sensor.weatherstation_rain_24h` | l/m² | Regen letzte 24h |
| `regen_gesamt` | `sensor.weatherstation_rain_total` | l/m² | Regen gesamt |

---

## Weitere Sensoren hinzufügen

Die JSON-Datei kann beliebig viele Werte enthalten. Du musst **keinen WebClient-Code ändern** –
füge einfach neue Felder hinzu und konfiguriere das Mapping in den WebClient-Settings unter `/settings`.

Beispiel PV-Anlage zusätzlich in `rest_command` oder `shell_command`:

```yaml
"pv_leistung": {{ states('sensor.pv_power') | float(0) }},
"batt_soc":    {{ states('sensor.battery_soc') | float(0) }}
```

```json
{ "JsonKey": "pv_leistung", "Label": "PV", "Unit": "kW", "Decimals": 2 }
```
