# Referenz

Zum Nachschlagen: Ports, Dateien, Befehle und die Gliederung der Einstellungen.

## Ports

| Port | Protokoll | Wofür |
|---|---|---|
| **5162** | TCP | Oberfläche von WebDesk (HTTP) |
| **5163** | TCP | Oberfläche über HTTPS (nur wenn eingerichtet) |
| **1799** | UDP | EXTUDP zwischen Node und WebDesk |
| **8001** | TCP | KISS/TCP am Node (nur wenn eingeschaltet) |

## Starten mit Parametern

| Parameter | Wirkung |
|---|---|
| `--reset-password` | Schaltet den Zugriffsschutz aus |
| `--reset-password NEUESPASSWORT` | Setzt ein neues Passwort |
| `MESHCOM_RESET_ACCESS=1` (Umgebungsvariable) | Wie `--reset-password`, für Docker ohne Shell |

Wichtige Werte der Konfigurationsdatei `appsettings.json` (oder als Umgebungsvariable `Meshcom__<Name>`, z. B.
`Meshcom__DeviceIp`):

| Name | Bedeutung |
|---|---|
| `DeviceIp` | IP-Adresse des Nodes |
| `MyCallsign` | Dein Rufzeichen mit SSID |
| `DataPath` | Ordner für Einstellungen und Daten |
| `LogPath` | Ordner für Logdateien |

## Bot-Befehle

`>` oder `--` direkt vor dem Befehl, siehe [Automatik](06-automation.md).

| Befehl | Antwort |
|---|---|
| `>help` | Liste aller Befehle |
| `>version` | Version von WebDesk |
| `>time` | Datum und Uhrzeit |
| `>mh` | Zuletzt gehörte Stationen |
| `>ping` | Signal und Weg des Pakets |
| `>rx` | Wie die Bot-Station dich empfängt |
| `>echo <Text>` | Schickt den Text zurück |
| `--weather` | Wetterdaten (nur mit eingerichteter Wetter-API) |

Dazu kommen die Befehle, die du selbst anlegst.

## Variablen

Die vollständige Tabelle der Variablen (`{mycall}`, `{callsign}`, `{date}`, `{time}`, `{locator}`, `{last-qso}`,
`{telemetry}`, `{my-eirp}` …) steht in [Nachrichten schreiben](04-writing-messages.md). Für Auto-Reply und Bot
gibt es zusätzlich `{srctype}` und `{srctype-label}`. Die Termin-Bake kennt `{title}`, `{event_date}`, `{event_time}`,
`{days_until}` und `{hours_until}`.

## Tasten

| Taste | Wirkung |
|---|---|
| **Enter** | Sendet die Nachricht |
| **Tab** | Zeigt in der Sendeleiste die ersetzten Variablen |

## Bedeutung der Statusanzeigen

| Anzeige | Bedeutung |
|---|---|
| 🔴 Kein Socket | WebDesk kann den UDP-Port nicht öffnen |
| 🟡 Warte auf Signal | Port offen, noch kein Paket vom Node |
| 🟢 Empfang OK | Pakete kommen an |
| ⚠️ Rufzeichen? | Das Rufzeichen (SSID) passt nicht zum Node |
| ● mit Uhrzeit | Nächste Bake oder Telemetrie |
| 🔔 / 🔕 | Töne an / stumm |
| 🔊 / 🔇 | Sprachansage an / aus |

Die Symbole der Zustellung (⏳ ✓ ✓✓ ☁️ ❌ 📬) stehen in [Die Oberfläche](03-interface.md).

## Einstellungen im Überblick

Die Einstellungen sind in fünf Reitern geordnet. Das Suchfeld oben findet eine Karte auch über Stichwörter.

| Reiter | Karten |
|---|---|
| **🔌 Verbindung** | Nodes, KISS-Hub, Station / HF-Parameter, Console |
| **📡 Senden & Automatik** | Auto-Reply, Bot, Bake, Termin-Bake, Telemetrie, Schnelltexte |
| **💬 Chat & Anzeige** | Chat & Monitor, Watchlist, Blacklist, Gruppen-Namen, Sprache, Erscheinungsbild, Web |
| **🔗 Integrationen** | Wetter-API, MQTT, Webhook, QRZ.com, KI, Datenbank |
| **🛠️ System** | Logging, Datenspeicherung, Zugriff von anderen Geräten, Zugriffsschutz, Datensicherung, Lizenz |

## Gruppen-Namen und Sprache

- Unter **Gruppen-Namen** vergibst du Anzeigenamen für Gruppennummern (z. B. `#262` → `DL`). Die Liste ist mit den
  offiziellen MeshCom-Gruppen vorbelegt und lässt sich ändern, exportieren und importieren.
- Die Oberfläche gibt es in **Deutsch, Englisch, Französisch, Italienisch und Spanisch**. Die Sprache wählst du unter
  **Sprache**. Sie gilt sofort. Das Handbuch gibt es bisher auf Deutsch und Englisch.
- Eigene Übersetzungen oder neue Sprachen legst du als JSON-Datei in `<DataPath>/languages/` ab (Anleitung:
  [translations.md](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/translations.md)).

## Weiterführende Anleitungen

- [Node-Verbindung](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/node-connection-de.md) und
  [KISS/TCP](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/kiss-tcp-guide-en.md)
- [Docker-Update](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/docker-update-de.md)
- [Telemetrie mit Home Assistant](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-telemetry.md) und
  [Ungelesen-Alarm](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-unread-alert.md)
- [README mit allen Funktionen](https://github.com/DH1FR/MeshcomWebDesk#readme) und
  [Neuigkeiten je Version](https://github.com/DH1FR/MeshcomWebDesk/releases)

Zurück zu [Willkommen](00-welcome.md).
