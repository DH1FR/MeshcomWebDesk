# Changelog

## [1.9.6] – in Entwicklung (dev)

### Features
- **TLS-Console-Tab**: Neuer Tab „🖥️ TLS Console" (rechts neben Suchen) für verschlüsselten Konsolenzugriff auf den MeshCom-Node über TLS (Standard-Port 2323, Device IP).
  - Nur sichtbar wenn in den Einstellungen aktiviert.
  - Verbindung wird über den Statusleisten-Schalter manuell aufgebaut und getrennt.
  - TLS mit selbst-signiertem Zertifikat (ECDHE-ECDSA); Fingerprint-Vertrauen direkt in den Einstellungen.
  - Passwort-Authentifizierung; Passwort wird verschlüsselt gespeichert (DPAPI).
  - Lokales Echo unterdrückt (Echo kommt vom Node), Eingaben mit CRLF gesendet.
  - Ausgabe-Buffer (max. 500 Zeilen) mit automatischem Scroll ans Ende.
  - **Pause**: Bildschirmausgabe anhalten (neue Zeilen werden weiter gepuffert, Scroll gestoppt).
  - **Manuelles Trennen**: „Trennen"-Button im Header der TLS-Console.
  - **Firmware-Link**: Link zu `https://github.com/icssw-org/MeshCom-Firmware` im Konsolenheader.
- **OTA-Update**: Button „🔄 OTA" in der TLS-Console sendet `--ota-update` an den Node.
  - Countdown-Dialog (5 Sek.) mit animiertem Fortschrittsbalken zeigt den Warte-Zeitraum an.
  - OTA-Webseite (`http://<DeviceIP>/`) öffnet sich nach dem Countdown automatisch im neuen Tab.
  - TLS-Console-Verbindung wird nach dem Öffnen der OTA-Seite automatisch getrennt.
- **TLS-Console-Statusindikator**: Statusleiste zeigt 🖥️ (grün, verbunden) bzw. 🖥️✗ (rot, getrennt) – analog zum Sprachansage-Toggle; Klick baut Verbindung auf bzw. trennt sie.
- **Einstellungen – TLS-Console-Sektion**: Neuer Abschnitt mit Aktivierungs-Checkbox, Port, Passwort, Zertifikat-Fingerprint (Trust & Save) und Hinweis, dass auf dem MeshCom-Node die Telnet-Konsole (`enable Telnet console`) aktiviert sein muss.
- **Lautsprechersymbol bleibt erhalten**: Der Sprachansage-Schalter in der Statusleiste bleibt nach einem Seitenaufbau/Reconnect sichtbar (war vorher kurzzeitig ausgeblendet, bis localStorage geladen wurde).

### Bugfixes
- **TelnetEnabled wurde nicht gespeichert**: `SettingsService.SaveMeshcomSettingsAsync` schrieb `TelnetEnabled` nicht in `appsettings.override.json` – der Wert ging nach jedem Neustart verloren. Behoben.
- **TelnetEnabled fehlte im Backup/Restore**: Beim Wiederherstellen einer Einstellungssicherung wurde `TelnetEnabled` nicht übertragen. Behoben.
- **Passwort-Authentifizierung TLS-Console**: DPAPI-Entschlüsselung für `TelnetPassword` fehlte beim Start – die Konsole erhielt den verschlüsselten `dp:`-String statt des Klartexts. Behoben.
- **Telemetrie-Doppelversand nach Neustart**: Der Telemetrie-Scheduler sendete nach einem Neustart innerhalb der geplanten Stunde erneut. `lastSentSlot` wird jetzt auf die aktuelle Stunde initialisiert. Behoben.

---

## [1.9.5] – veröffentlicht

### Features
- **MsgId im Monitor**: Eingehende Nachrichten zeigen jetzt die Nachrichten-ID (`msg_id`) im Monitor an.
- **Toast-Bezeichnung**: Toast-Anzeige für Watchlist und CQ-Erkennung verwendet jetzt „empfangen" statt „gehört".
- **Eigene Nachrichten linksbündig**: Neue Option `OwnMessagesAlignLeft` – eigene gesendete Nachrichten können optional linksbündig (statt rechtsbündig) angezeigt werden; konfigurierbar in den Einstellungen.
- **Watchlist-TTS**: Sprachansage für Watchlist-Treffer wird pro Rufzeichen maximal einmal alle 5 Minuten ausgelöst (bisher: bei jedem Paket).

### Bugfixes
- **{last-qso} zeigte aktuelles statt vorheriges QSO**: `GetLastQsoTimeDbOnlyAsync`, `GetLastQsoTimeAsync` und `GetLastQsoTimeInternalAsync` erhalten einen optionalen `before`-Parameter (`AND timestamp < @before`). `ExpandVariablesAsync` übergibt `DateTime.Now` (Auto-Reply) bzw. `message.Timestamp` (Bot). Kein vorheriges QSO → Ausgabe `„kein QSO"` statt leer.
- **Bot: `---===` fälschlich als Befehl erkannt**: `BotCommandService.IsCommand()` prüft nun, ob nach `--` bzw. em-dash ein Buchstabe folgt (`char.IsLetter`). Dekorations-Strings wie `---===` (WebDesk-Ident-Auto-Reply anderer Stationen) werden nicht mehr als Bot-Befehl behandelt.
- **Direkt-Tab ohne Voice nicht sichtbar**: `HandleNewDirectTab` in `Chat.razor` rief `StateHasChanged` nur auf wenn Voice aktiv war – der neu angelegte Tab erschien nicht in der UI. Fix: `StateHasChanged` wird jetzt immer aufgerufen; TTS-Ansage nur wenn Voice aktiv.
- **QRZ Race Condition (bis zu 9 parallele Requests)**: `_inflightLookups` (`ConcurrentDictionary<string, Task<QrzInfo?>>`) dedupliziert In-Flight-Requests – alle parallelen `LookupAsync`-Aufrufe für dasselbe Rufzeichen teilen sich jetzt eine einzige `Task`.

---

## [1.9.4] – veröffentlicht

### Features
- **Variable {last-qso}**: Neue Template-Variable mit Datum und Uhrzeit des letzten direkten QSOs mit dem aktuellen Rufzeichen (DB-first + In-Memory-Fallback). Verfügbar in Auto-Reply, Bot, Beacon und Quick Texts.
- **Nachricht in Zwischenablage kopieren**: Im Direkt-Nachrichten-Tab erscheint beim Hovern über eine Nachricht ein 📋-Button zum Kopieren des Nachrichtentexts in die Zwischenablage (JS Clipboard API).
- **Station / HF-Parameter** (neue Settings-Section):
  - TX-Leistung (dBm), Kabeltyp, Kabellänge, Antennengewinn (dBi), **Antennentyp** (Freitextfeld, z. B. Dipol, Yagi 3-El.), Antennenhöhe, Frequenz und Systemreserve (dB)
  - **15 Koaxkabeltypen** (alle 50 Ω) in drei Dämpfungsgruppen: LMR-600, LMR-400, H-2000 Flex, Ecoflex 15, CFD-400, Ecoflex 10, Aircell 7, LMR-240, H-155, LMR-200, RG-213, RG-8/U, RG-8X, RG-58, RG-174
  - **Manuelle Dämpfungseingabe**: Auswahl „Manuell eingeben …" zeigt ein zusätzliches Eingabefeld für die Dämpfung in dB/10 m (beliebiges Kabel)
  - Live-Anzeige von **EIRP** (P_TX − Kabeldämpfung + Antennengewinn) und **theoretischer Freiraumreichweite** (Systemreserve konfigurierbar, Standard 30 dB)
  - Alle Parameter werden im Backup gespeichert und sind rückwärtskompatibel
- **FSPL-Reichweiten-Kreis** auf der Karte: beim Aktivieren des Coverage-Layers erscheint zusätzlich ein gelber Kreis mit der theoretischen Freiraumreichweite (EIRP + Frequenz + Systemreserve); Legende unterscheidet „Gemessen (Convex Hull)" und „FSPL-Reichweite (theor.)"
- **Stationsvariablen** – neue Template-Variablen für Station-/HF-Daten, überall verfügbar (Auto-Reply, Bot, Beacon, Quick Texts, Sendeleiste):
  - `{my-tx-power}` – TX-Leistung (z. B. 22 dBm)
  - `{my-eirp}` – berechnete EIRP (z. B. 23.50 dBm)
  - `{my-antenna}` – Antennengewinn (z. B. 2.5 dBi)
  - `{my-antenna-type}` – Antennentyp (z. B. Dipol)
  - `{my-antenna-height}` – Antennenhöhe (z. B. 10 m)
  - `{my-freq}` – Betriebsfrequenz (z. B. 433.175 MHz)
- **CQ-Erkennung**: Eingehende Gruppen-Nachrichten werden automatisch auf CQ-Rufe geprüft (case-insensitiver Regex, kein False-Positive bei Wörtern wie „FREQUENCY"):
  - Eigenes Rufzeichen wird unterdrückt; nur Gruppen aus dem aktiven Gruppenfilter werden ausgewertet
  - **Toast-Anzeige** (gelb, analog Watchlist-Toast) mit Rufzeichen, Gruppe und Alter; automatisches Verschwinden nach 60 Sekunden
  - **CQ-Beep**: Morse-Muster **CQ CQ** in CW-Stil (700 Hz, 80 ms Einheit, korrektes dit/dah/Wortpausen-Timing); nur wenn Ton aktiv
  - **TTS-Ansage**: z. B. „C Q C Q von Delta Hotel Eins Foxtrot Romeo in Gruppe 2 6 2" – nur wenn Sprachansagen aktiv; folgt der App-Sprache (DE/EN)
- **Toast-Stapelung**: Watchlist- und CQ-Toast werden in einem gemeinsamen Container vertikal gestapelt, sodass beide gleichzeitig sichtbar sind (zuvor Überlagerung)
- **OpenAI Usage-Link**: Zusätzlicher Link zu `https://platform.openai.com/usage` im KI-Bereich ergänzt.

### Bugfixes
- **OpenAI Guthaben-Link**: Migriert von veralteten Dashboard-Endpunkten auf `/v1/organization/costs`.
- **CQ-Beep Timing**: Korrektes Morse-Timing (Inter-Element 1u, Inter-Zeichen 3u, Wortpause 7u) ohne doppelte Lücken.
- **FSPL-Kreis**: Kontrast erhöht; Kreis wird jetzt auch in `UpdateMarkersAsync` neu gezeichnet (zuvor nur in `ToggleCoverageAsync`).
- **Station/HF-Felder im Backup**: `TxPowerDbm`, `CableType`, `CableLengthM`, `AntennaGainDbi`, `AntennaHeightM`, `FrequencyMhz` fehlten in der Backup-Serialisierung.

---

## [1.9.3] – veröffentlicht

### Features
- **Variable {telemetry}**: Neue Template-Variable für den aktuellen Telemetrie-String der eigenen Station; verfügbar in Auto-Reply, Bot, Beacon und Quick Texts.
- **Maidenhead-Locator lowercase fix**: QTH-Locator wird jetzt normalisiert ausgegeben (z. B. `JN48qn` statt `JN48QN`).
- **@-Mention**: Neben jedem Rufzeichen in Monitor und Gruppen-/Broadcast-Nachrichten erscheint ein `@`-Button; Klick fügt `@RUFZEICHEN` in das Texteingabefeld ein.
- **Globaler Such-Button**: Neuer 🔎-Button in der Tab-Leiste öffnet eine KI-Suche über alle Direkt-QSOs gleichzeitig (kanalübergreifend).
- **Statusleiste 3-stufig adaptiv**: Drei Layout-Stufen (iPhone / iPad / Desktop) mit `ResizeObserver` + `MutationObserver`; `getBoundingClientRect` für zuverlässiges Overflow-Erkennen auch unter Safari/iOS.
- **Node-Firmware/-HW persistent**: `NodeHwId` und `NodeFirmware` werden über Neustarts hinweg gespeichert und in der Statusleiste angezeigt.
- **Variablen `{node-firmware}` / `{node-hw}`**: Neue Template-Variablen für Firmware-Version und Hardware-Typ des verbundenen MeshCom-Knotens; verfügbar in Bot, Quick Texts und Sendeleiste.
- **Sequenznummer im Monitor**: Ausgehende Nachrichten zeigen die zugewiesene Sequenznummer sobald der Knoten sie zurückmeldet.

### Bugfixes
- **Statusleiste Overflow-Schleife**: `ResizeObserver`-Callback aktualisiert mit 120 ms Verzögerung (`setTimeout`) – verhindert Layout-Feedback-Schleifen unter iOS/iPad.
- **iOS/Safari AudioContext**: Unlock auf `touchstart`; TTS wird auf iOS übersprungen und nur der AudioContext-Beep verwendet.
- **iOS/Safari TTS**: Speech-Synthesis wird beim ersten Touch freigegeben; Race-Condition zwischen `cancel()` und `speak()` behoben.
- **ACK-Fallback Zeitfenster**: ACK-Fallback für Snapshot-Nachrichten auf maximal 10 Minuten begrenzt – verhindert, dass alte Nachrichten neue ACKs verbrauchen.
- **Reichweiten-Wolke (mehrere Fixes)**:
  - Wolke zeigt nur direkt gehörte Stationen (HopCount == 0) bis max. 500 km; bei weniger als 3 Direktverbindungen Fallback auf alle GPS-Stationen.
  - Legenden-Rechteck wurde nicht angezeigt (fehlender `display:inline-block`).
- **`hw_id`-Parsing**: Robustere Auswertung (Number + String); `NodeHwId` wird unabhängig von `NodeFirmware` gesetzt.

---

## [1.9.2] – veröffentlicht

### Performance
- **Einstellungen**: Section-Inhalte werden nur gerendert wenn die Section geöffnet ist (`@if (IsOpen(...))` Guards für alle 18 Sections). Beim ersten Öffnen der Settings-Seite werden nur die Section-Header gerendert statt alle ~149 Bindings → deutlich schnelleres Laden.
- **MH-Liste & Karte**: Neues `OnMhChange`-Event in `ChatService` – MH-Liste und Karte reagieren nur noch auf MH-relevante Updates statt auf jedes eingehende Paket.
- **Karte**: Marker-Neuzeichnung wird um 400 ms debounced, um mehrfache Neuzeichnungen bei schnell eintreffenden Paketen zu vermeiden.
- **MH-Liste**: QRZ.com-Abfragen werden nur noch für Rufzeichen ausgelöst, die noch nicht im Cache sind.

### Features
- **MH-Liste Max. Alter**: Eingabe wurde von Tagen auf **Stunden** umgestellt (`MhMaxAgeHours`). Bestehende Werte werden automatisch migriert (Tage × 24).
- **MH-Bereinigung beim Speichern**: Nach dem Speichern der Einstellungen werden veraltete MH-Einträge sofort gelöscht (nicht erst beim nächsten Intervall).
- **MH-Bereinigung beim Start**: Beim Programmstart werden veraltete MH-Einträge aus dem gespeicherten Snapshot sofort bereinigt.
- **Statusleiste responsive**: Die Statusleiste passt sich dynamisch der verfügbaren Breite an. Elemente werden stufenweise ausgeblendet wenn der Platz nicht ausreicht (`ResizeObserver` + `MutationObserver`).

### Bugfixes
- **MH Max. Alter**: Der Wert `MhMaxAgeHours` wurde nach einem Seitenwechsel auf 0 zurückgesetzt – Fehler in `SettingsService` behoben, der Wert wird jetzt korrekt in `appsettings.override.json` gespeichert.
- **Karte – Reichweiten-Wolke**: Die Reichweiten-Wolke (Convex Hull) wurde nach einem MH-Update oder MH-Purge nicht neu berechnet und blieb leer. `UpdateMarkersAsync()` ruft jetzt `setCoverage` automatisch neu auf wenn die Anzeige aktiv ist.

---

## [1.9.1] – veröffentlicht

### Features
- TTS-Ansage „Neue Nachricht von" nur einmal pro Rufzeichen innerhalb von 5 Minuten.
- Sprachansage „Neue Nachricht von" hat Vorrang vor Watchlist-TTS.
- Welcome-Dialog überarbeitet.
- Beta-Label bei Datenbank und MQTT entfernt.

---

## [1.9.0] – veröffentlicht

### Features
- Erste Veröffentlichung dieser Hauptversion.