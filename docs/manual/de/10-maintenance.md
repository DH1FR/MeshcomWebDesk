# Sicherung, Wartung und Fehlersuche

## Wo WebDesk seine Daten ablegt

| Was | Wo |
|---|---|
| Einstellungen, die du in der Oberfläche speicherst | `<DataPath>/appsettings.override.json` |
| Chats, MH-Liste und Monitor | im Ordner `<DataPath>` (WebDesk speichert beim Beenden und alle 5 Minuten) |
| Zugriffsschutz-Passwort (Hash) | `<DataPath>/access.json` |
| Schlüssel | `<DataPath>/keys` |
| Logdateien | im Ordner `logs` (einstellbar mit `LogPath`) |
| Zusätzliche Sprachdateien | `<DataPath>/languages/` |

Ohne eigene Einstellung liegen `data/`, `logs/` und die Schlüssel **neben dem Programm**. Unter Docker sind es die Ordner
`./data`, `./logs` und `./keys` neben der `docker-compose.yml`. Beim Beenden speichert WebDesk den Zustand und stellt ihn beim
Start wieder her. Einstellungen, die du in der Oberfläche änderst, schreibt WebDesk in die Datei `appsettings.override.json`.
Die mitgelieferte `appsettings.json` bleibt unverändert (unter Docker darf sie schreibgeschützt sein).

## Einstellungen sichern und wiederherstellen

Unter **Einstellungen → System → Datensicherung** (ganz unten) sicherst du **alle Einstellungen** in eine verschlüsselte Datei:

- Du vergibst ein **Passwort**. Die Datei heißt `MeshComWebDesk-settings.enc`, den Namen kannst du vor dem Export ändern.
- Sie enthält auch Passwörter und Schlüssel (QRZ.com, MQTT, KI, Datenbank …), verschlüsselt mit **AES-256**. Sie lässt sich auf
  einem anderen Rechner oder Betriebssystem wiederherstellen.
- **Wiederherstellen:** Datei auswählen, Passwort eingeben, WebDesk prüft sie, speichert die Einstellungen und lädt die Seite
  neu. Bei falschem Passwort, beschädigter Datei oder fehlenden Angaben erscheint eine klare Fehlermeldung.

> ⚠️ **Das Passwort wird nirgends gespeichert.** Geht es verloren, lässt sich die Datei nicht mehr öffnen.

Nicht in dieser Sicherung enthalten ist das Passwort des Zugriffsschutzes (`access.json`, siehe
[Sicherheit](09-security.md)).

## Aktualisieren

Das Aktualisieren steht im Kapitel [Schnellstart und Installation](01-install.md). Ohne Docker aktualisiert sich WebDesk
auf der Seite **Über → Aktualisieren / Sicherungen** selbst und legt vorher eine Sicherung an.

## Daten löschen

Die Seite **Löschen** entfernt **alle gespeicherten Chats, Monitor-Einträge und die MH-Liste**. Das lässt sich nicht
rückgängig machen, WebDesk fragt vorher nach. Deine Einstellungen bleiben unverändert.

## Konsole: Node direkt bedienen

Die Seite **NET Console** (`/telnet`) gibt dir die Kommandozeile des Nodes im Browser. Sie erscheint in der Menüleiste, sobald
du die Konsole unter **Einstellungen → Verbindung → Console** einschaltest. Dort wählst du auch den Weg:

- **TLS-Konsole:** über das Netzwerk, verschlüsselt. Praktisch, wenn der Node nicht per USB erreichbar ist (z. B. im
  Freien). Sie braucht Firmware **ab v4.35p.05.13**. Am Node schaltest du sie mit `--tlsconsole on` ein und setzt ein Passwort mit
  `--passwd <Passwort>`. In WebDesk trägst du die Device-IP und das Passwort ein. Beim ersten Verbinden zeigt WebDesk das
  Zertifikat des Nodes an, mit **Trust & Save** übernimmst du es.
- **Serielle Konsole (USB):** direkt am USB-Anschluss des Nodes. Du wählst Port und Baudrate. Unter Windows nimmst du den
  einfachen Namen (`COM3`), unter Linux und Docker reichst du das Gerät (`/dev/ttyUSB0`) in den Container durch.

Die Konsole hat Knöpfe zum **Anhalten** der Anzeige, **Löschen** der Anzeige, für ein **OTA-Update** des Nodes (mit
Countdown) und zum **Neustart** des Nodes (mit Rückfrage). Der **Befehlshelfer** hilft, die Befehle des Nodes zu finden.

## Logdateien

WebDesk schreibt tägliche Logdateien in den Ordner `logs` (wie lange sie aufbewahrt werden, stellst du unter
**Einstellungen → System → Logging** ein). Optional gibt es ein Protokoll des UDP-Verkehrs für die Analyse. Unter Linux mit
systemd zeigt `journalctl -u meshcom-webdesk -f` das laufende Log.

## Fehlersuche

### Es kommen keine Nachrichten an

Schau auf den **UDP-Indikator** in der Statusleiste und gehe die Liste im Kapitel
[Node verbinden](02-connect-node.md) (Abschnitt „Prüfen, ob es funktioniert“) durch. Die häufigsten Ursachen:

- Ziel-IP am Node falsch oder eine Broadcast-Adresse (ab Firmware 4.40a.10 nicht mehr erlaubt)
- UDP-Port 1799 in der Firewall blockiert
- Die **SSID** im Rufzeichen passt nicht zum Node (Warnung **⚠️ Rufzeichen?**)
- Node und Rechner nicht im selben Netz
- Ein zweites Programm belegt den Port (🔴 *Kein Socket*)

Der Knopf **🔍 Einstellungen prüfen** an der Node-Karte nennt konkrete Fehler.

### Ich komme vom Handy nicht auf WebDesk

- Der Rechner und das Handy müssen im **selben Netz** sein, und die Adresse lautet `http://<IP-des-Rechners>:5162`.
- Prüfe, ob eine Firewall den Port **5162** blockiert.
- Bei eingeschaltetem Zugriffsschutz musst du dich anmelden ([Sicherheit](09-security.md)).

### Die App lässt sich nicht installieren, Desktop-Meldungen kommen nicht

Beides braucht eine **sichere Verbindung** (HTTPS oder `localhost`), siehe [Sicherheit](09-security.md). Beim Handy muss
außerdem das Zertifikat vertrauenswürdig installiert sein.

### Ein Telemetrie-Wert kommt nicht beim Node an

Der Node übernimmt Werte nur, wenn er **keine echten Sensoren** hat und die Firmware das unterstützt. WebDesk setzt das
Senden aus, wenn innerhalb von 20 Sekunden kein passendes Echo vom Node kommt. Die Einstellung bleibt aber erhalten.
Mehr dazu in [Automatik](06-automation.md).

### Passwort vergessen

Siehe [Sicherheit](09-security.md) (`--reset-password`).

### Etwas funktioniert nicht und ich weiß nicht warum

1. Öffne den **Monitor** und sieh nach, ob überhaupt Pakete des Nodes ankommen.
2. Schau in die Logdatei (siehe oben).
3. Gib in einer Fehlermeldung die **WebDesk-Version** (Seite **Über**), die **Firmware-Version** des Nodes und deine
   Einstellungen an, wenn du auf GitHub ein Problem meldest.

Weiter mit der [Referenz](11-reference.md).
