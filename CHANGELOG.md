# Changelog

## [1.9.2] – in Entwicklung (dev)

### Performance
- **Einstellungen**: Section-Inhalte werden nur gerendert wenn die Section geöffnet ist (@if (IsOpen(...)) Guards für alle 18 Sections). Beim ersten Öffnen der Settings-Seite werden nur die Section-Header gerendert statt alle ~149 Bindings → deutlich schnelleres Laden.
- **MH-Liste & Karte**: Neues OnMhChange-Event in ChatService – MH-Liste und Karte reagieren nur noch auf MH-relevante Updates statt auf jedes eingehende Paket.
- **Karte**: Marker-Neuzeichnung wird um 400 ms debounced, um mehrfache Neuzeichnungen bei schnell eintreffenden Paketen zu vermeiden.
- **MH-Liste**: QRZ.com-Abfragen werden nur noch für Rufzeichen ausgelöst, die noch nicht im Cache sind.

### Features
- **MH-Liste Max. Alter**: Eingabe wurde von Tagen auf **Stunden** umgestellt (MhMaxAgeHours). Bestehende Werte werden automatisch migriert (Tage × 24).
- **MH-Bereinigung beim Speichern**: Nach dem Speichern der Einstellungen werden veraltete MH-Einträge sofort gelöscht (nicht erst beim nächsten Intervall).
- **MH-Bereinigung beim Start**: Beim Programmstart werden veraltete MH-Einträge aus dem gespeicherten Snapshot sofort bereinigt.
- **Statusleiste responsive**: Die Statusleiste passt sich dynamisch der verfügbaren Breite an. Elemente werden stufenweise ausgeblendet wenn der Platz nicht ausreicht (ResizeObserver + MutationObserver).
- **Variable {last-qso}**: Neue Template-Variable mit Datum und Uhrzeit des letzten direkten QSOs mit dem aktuellen Rufzeichen (DB-first + In-Memory-Fallback). Verfügbar in Auto-Reply, Bot, Beacon und Quick Texts.
- **Nachricht in Zwischenablage kopieren**: Im Direkt-Nachrichten-Tab erscheint beim Hovern über eine Nachricht ein 📋-Button zum Kopieren des Nachrichtentexts in die Zwischenablage (JS Clipboard API).
- **Station / HF-Parameter** (neue Settings-Section):
  - TX-Leistung (dBm), Kabeltyp, Kabellänge, Antennengewinn (dBi), **Antennentyp** (Freitextfeld, z. B. Dipol, Yagi 3-El.), Antennenhöhe, Frequenz und Systemreserve (dB)
  - **15 Koaxkabeltypen** (alle 50 Ω) in drei Dämpfungsgruppen: LMR-600, LMR-400, H-2000 Flex, Ecoflex 15, CFD-400, Ecoflex 10, Aircell 7, LMR-240, H-155, LMR-200, RG-213, RG-8/U, RG-8X, RG-58, RG-174
  - **Manuelle Dämpfungseingabe**: Auswahl „Manuell eingeben …“ zeigt ein zusätzliches Eingabefeld für die Dämpfung in dB/10 m (beliebiges Kabel)
  - Live-Anzeige von **EIRP** (P_TX − Kabeldämpfung + Antennengewinn) und **theoretischer Freiraumreichweite** (Systemreserve konfigurierbar, Standard 30 dB)
  - Alle Parameter werden im Backup gespeichert und sind rückwärtskompatibel
- **FSPL-Reichweiten-Kreis** auf der Karte: beim Aktivieren des Coverage-Layers erscheint zusätzlich ein gelber Kreis mit der theoretischen Freiraumreichweite (EIRP + Frequenz + Systemreserve); Legende unterscheidet „Gemessen (Convex Hull)“ und „FSPL-Reichweite (theor.)“
- **Stationsvariablen** – neue Template-Variablen für Station-/HF-Daten, überall verfügbar (Auto-Reply, Bot, Beacon, Quick Texts, Sendeleiste):
  - {my-tx-power} – TX-Leistung (z. B. 22 dBm)
  - {my-eirp} – berechnete EIRP (z. B. 23.50 dBm)
  - {my-antenna} – Antennengewinn (z. B. 2.5 dBi)
  - {my-antenna-type} – Antennentyp (z. B. Dipol)
  - {my-antenna-height} – Antennenhöhe (z. B. 10 m)
  - {my-freq} – Betriebsfrequenz (z. B. 433.175 MHz)
- **CQ-Erkennung**: Eingehende Gruppen-Nachrichten werden automatisch auf CQ-Rufe geprüft (case-insensitiver Regex, kein False-Positive bei Wörtern wie „FREQUENCY“):
  - Eigenes Rufzeichen wird unterdrückt; nur Gruppen aus dem aktiven Gruppenfilter werden ausgewertet
  - **Toast-Anzeige** (gelb, analog Watchlist-Toast) mit Rufzeichen, Gruppe und Alter; automatisches Verschwinden nach 60 Sekunden
  - **CQ-Beep**: Morse-Muster **CQ CQ** in CW-Stil (700 Hz, 80 ms Einheit, korrektes dit/dah/Wortpausen-Timing); nur wenn Ton aktiv
  - **TTS-Ansage**: z. B. „C Q C Q von Delta Hotel Eins Foxtrot Romeo in Gruppe 2 6 2“ – nur wenn Sprachansagen aktiv; folgt der App-Sprache (DE/EN)
- **Toast-Stapelung**: Watchlist- und CQ-Toast werden in einem gemeinsamen Container vertikal gestapelt, sodass beide gleichzeitig sichtbar sind (zuvor Überlagerung)

### Bugfixes
- **MH Max. Alter**: Der Wert `MhMaxAgeHours` wurde nach einem Seitenwechsel auf 0 zurückgesetzt – Fehler in `SettingsService` behoben, der Wert wird jetzt korrekt in `appsettings.override.json` gespeichert.
- **Karte – Reichweiten-Wolke**: Die Reichweiten-Wolke (Convex Hull) wurde nach einem MH-Update oder MH-Purge nicht neu berechnet und blieb leer. `UpdateMarkersAsync()` ruft jetzt `setCoverage` automatisch neu auf wenn die Anzeige aktiv ist.
- **OpenAI Guthaben-Link**: Migriert von veralteten Dashboard-Endpunkten auf `/v1/organization/costs`; zusätzlicher Link zu `https://platform.openai.com/usage` ergänzt.
- **CQ-Beep Timing**: Korrektes Morse-Timing (Inter-Element 1u, Inter-Zeichen 3u, Wortpause 7u) ohne doppelte Lücken.

---

## [1.9.1] – veröffentlicht

### Features
- TTS-Ansage „Neue Nachricht von“ nur einmal pro Rufzeichen innerhalb von 5 Minuten.
- Sprachansage „Neue Nachricht von“ hat Vorrang vor Watchlist-TTS.
- Welcome-Dialog überarbeitet.
- Beta-Label bei Datenbank und MQTT entfernt.

---

## [1.9.0] – veröffentlicht

### Features
- Erste Veröffentlichung dieser Hauptversion.
