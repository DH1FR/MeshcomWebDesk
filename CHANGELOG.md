# Changelog

## [1.9.2] – in Entwicklung (dev)

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
