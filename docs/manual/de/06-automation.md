# Automatik

WebDesk kann Nachrichten selbst senden: Baken, Terminankündigungen, Antworten, Bot-Befehle und Telemetrie. Alles
steht unter **Einstellungen → Senden & Automatik** und gilt **sofort, ohne Neustart**. Was WebDesk beim Senden ersetzen
soll (z. B. `{mycall}`), steht in [Nachrichten schreiben](04-writing-messages.md).

> ℹ️ Alle diese Funktionen laufen über den **Primär-Node**. Eine LoRa-Nachricht darf höchstens 149 Zeichen lang sein;
> längere Bot-Antworten und Baken-Texte teilt WebDesk auf mehrere Pakete auf.

## Bake (Beacon)

Die Bake sendet regelmäßig einen festen Text an eine Gruppe.

- Du stellst **Text**, **Zielgruppe** und **Intervall** ein. Das Intervall wird in ganzen Stunden angegeben, **mindestens
  8 Stunden**. Zu kleine Werte korrigiert WebDesk. Die erste Sendung kommt nach **einem vollen Intervall**, nicht bei jedem
  Neustart.
- Im Text gehen `{version}`, `{mycall}`, `{mylocator}`, `{date}`, `{time}` und `{telemetry}`.
- In der Statusleiste zeigt ein pulsierender Punkt ● die nächste Sendezeit (gelb, wenn sie in weniger als 10 Minuten ist).
- Der Knopf **Bake jetzt senden** sendet sofort zum Testen.
- Die Bake erscheint im Monitor und im Tab der Gruppe.

## Termin-Bake

Die Termin-Bake kündigt wiederkehrende Termine an, zum Beispiel den Ortsverbandsabend, vorher und zur Uhrzeit.

- Du kannst **beliebig viele Einträge** anlegen und jeden einzeln ein- oder ausschalten.
- **Wiederholung:** einmalig, wöchentlich, alle zwei Wochen, monatlich an einem festen Tag, an einem n-ten Wochentag
  (z. B. 2. Dienstag) oder am letzten Wochentag des Monats.
- **Vorlaufzeiten** als Liste, z. B. `3d, 24h, 2h` (`d` Tage, `h` Stunden, `m` Minuten; ohne Einheit gelten Stunden).
  Auf Wunsch kommt zusätzlich eine Ankündigung zur Terminzeit.
- Im Text gehen `{title}`, `{event_date}`, `{event_time}`, `{days_until}` und `{hours_until}`, zum Beispiel
  `📅 {title} am {event_date} um {event_time} – in {days_until} Tagen!`
- Die Einstellung zeigt die **nächste berechnete Sendezeit** als Vorschau.
- Der Text kann auch von einem **externen Skript** kommen (Häkchen **⚙️ Extern**). Das Skript erhält die Termindaten als JSON
  und gibt den Text aus. Ist die Ausgabe leer, entfällt diese Ankündigung. Die Voraussetzungen sind dieselben wie bei den
  Bot-Befehlen (siehe unten).

## Auto-Reply

Der Auto-Reply antwortet automatisch, wenn dir **jemand zum ersten Mal** schreibt (ein neuer Direkt-Tab entsteht).

- Du schreibst den Antworttext selbst. Variablen wie `{mycall}`, `{callsign}`, `{mylocator}`, `{date}` oder `{version}` sind
  erlaubt.
- Beispiel: `MeshCom WebDesk V{version} – QTH: {mylocator}`
- Mit dem **Testknopf** sendest du den Text sofort an ein beliebiges Rufzeichen.

## Bot: Befehle per Direktnachricht

Mit dem Bot können andere Stationen WebDesk per **Direktnachricht** etwas fragen.

**So wird ein Befehl erkannt:** Die Nachricht beginnt mit `>`, `--` (zwei Bindestriche) oder `—` (Geviertstrich, wie ihn
viele Handy-Tastaturen schreiben) und **direkt danach einem Buchstaben**: `>ping` und `--ping` sind dasselbe. Das gilt,
weil manche Geräte, etwa das LilyGo T-Deck, kein `>` haben. Ein bloßes `ping` ohne Zeichen davor ist normaler Chat-Text.

| Befehl | Antwort |
|---|---|
| `>help` | Liste aller Befehle |
| `>version` | `MeshcomWebDesk vX.Y.Z` |
| `>time` | Aktuelles Datum und Uhrzeit |
| `>mh` | Anzahl und Rufzeichen der zuletzt gehörten Stationen |
| `>ping` | Antwort mit Rufzeichen, Signalstärke, Weg über Relais und Empfangszeit |
| `>rx` | Wie die Bot-Station **dich** empfängt: direkt oder über ein Relais, mit RSSI und SNR |
| `>echo <Text>` | Schickt den Text zurück |

Auch bei **ausgeschaltetem Bot** (Standard) beantwortet WebDesk `>ping`, `>rx`, `>version` und `>help`.

**Eigene Befehle** legst du unter **Einstellungen → Senden & Automatik → Bot** an: Name (ohne `>`), Antworttext
(mit Variablen) und eine kurze Beschreibung für `>help`. Der Knopf **Testen** führt einen Befehl lokal aus und zeigt die
Antwort samt ersetzten Variablen, ohne etwas zu senden. Mit **Export** und **Import** sicherst du die Befehle in einer
Datei oder gibst sie weiter.

**Externe Skripte ⚙️:** Statt eines festen Textes kann ein Befehl ein Programm oder Skript starten (`.exe`, `.ps1`,
`.bat`, `.cmd` oder `.py`). Es erhält Absender, Argumente, Signalwerte und Stationsdaten als **JSON über die
Standardeingabe** und schreibt die Antwort auf die **Standardausgabe**. Eine leere Ausgabe oder ein Fehler bedeutet:
keine Antwort. Die Skripte liegen im Ordner für externe Befehle (bei Docker `./bot-scripts`), ein Beispiel ist
`bot-command-example.py`. Zusätzlich gilt ein einstellbares Zeitlimit (Standard 10 Sekunden).

## Telemetrie

Mit der Telemetrie sendest du Messwerte (Temperatur, Luftdruck, Feuchte …) an eine Gruppe oder ein Rufzeichen und/oder an
den Node.

- **Quelle:** eine JSON-Datei oder ein Programm, das die Werte per `POST /api/telemetry` an WebDesk schickt (z. B. Home
  Assistant, Node-RED). Der Zugang lässt sich mit einem API-Schlüssel (`X-Api-Key`) schützen.
- **Zuordnung:** In einer Tabelle ordnest du jedem Schlüssel Bezeichnung, Einheit und Nachkommastellen zu. Eine **Rolle**
  (Temperatur, Feuchte, Luftdruck …) bestimmt, wo der Wert in der Karten-Anzeige und in der Node-Telemetrie landet.
- **Zwei Wege:** als **Chat-Text** (`TM: 🌡=10.7C 🧭=1022hPa …`) nach Zeitplan und als **native Node-Telemetrie** (EXTUDP)
  bei Änderung eines Werts. Zu lange Texte werden in `TM1:`, `TM2:` … aufgeteilt.
- Der Node übernimmt die Werte nur, wenn er **keine echten Sensoren** eingebaut hat und die Firmware das unterstützt. WebDesk
  prüft das: Kommt innerhalb von 20 Sekunden kein passendes Echo, wird das Senden nur zur Laufzeit ausgesetzt, die
  Einstellung bleibt.
- Eine Vorschau in den Einstellungen zeigt die aktuellen Werte und die fertige Nachricht, und ein Knopf sendet sofort zum
  Testen.

Anleitungen dazu:
[Home Assistant](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-telemetry.md),
[praktische Einrichtung](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/ext_udp_telemetry_guide.md) und
[Protokoll](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/ext_udp_telemetry.md) (alle englisch bzw.
technisch).

## Wetterdaten

Unter **Einstellungen → Integrationen → Wetter-API** holt WebDesk Messwerte von einer Wetterplattform
(**AWEKAS**, **Weather Underground** oder eine **Simulation** zum Ausprobieren) und stellt sie der Telemetrie zur
Verfügung. Das Abfrage-Intervall liegt zwischen 5 und 60 Minuten. Die Feldnamen (z. B. `temp_out`, `humidity_out`,
`pressure_rel`) trägst du in der Telemetrie-Zuordnung ein. Der Bot-Befehl `--weather` meldet Anbieter, letzte Werte und
Zeitpunkt.

## Station und Funkparameter

Unter **Einstellungen → Verbindung → Station / HF-Parameter** gibst du Sendeleistung, Kabel, Antenne, Antennenhöhe,
Frequenz und eine Systemreserve an. WebDesk berechnet daraus die **EIRP** und die **theoretische Reichweite im freien
Raum**. Die Werte stehen als Variablen (`{my-tx-power}`, `{my-eirp}`, `{my-antenna}`, `{my-freq}` …) in Texten zur
Verfügung, und die Reichweite erscheint als Kreis auf der [Karte](07-map.md).

Weiter mit [Karte und Reichweite](07-map.md).
