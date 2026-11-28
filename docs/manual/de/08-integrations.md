# Integrationen

Unter **Einstellungen → Integrationen** verbindest du WebDesk mit anderen Diensten. Alles ist **optional** und
standardmäßig aus. Änderungen gelten sofort, ohne Neustart.

> 🔒 Passwörter und Schlüssel (QRZ.com, MQTT, KI, Datenbank) speichert WebDesk **verschlüsselt** in den Einstellungen.

## QRZ.com: Name und Ort

Mit einem kostenlosen [QRZ.com](https://www.qrz.com/register)-Konto (Benutzername und Passwort, kein eigener Schlüssel)
holt WebDesk **Vorname und Wohnort** der Funkamateure.

- Ohne SSID gesucht: `DH1FR-2` wird als `DH1FR` nachgeschlagen. Jedes Rufzeichen wird pro Sitzung nur einmal abgefragt.
- Die Angaben erscheinen in der **MH-Liste** (Spalte Name / Ort), als **Hinweis** über Rufzeichen im Chat und im Monitor und im
  **Fenster auf der Karte**. Sie stehen auch als `{dest-name}` und `{dest-loc}` für Texte zur Verfügung.
- Ein Testknopf schlägt dein eigenes Rufzeichen nach, ein anderer leert den Zwischenspeicher.
- Fehler (falsches Passwort, Netzfehler, unbekanntes Rufzeichen) landen als Warnung in der Logdatei.

## Datenbank

WebDesk kann alle Monitor-Daten dauerhaft in einer Datenbank speichern:

- **MySQL / MariaDB** (eine Zeile pro Monitor-Eintrag)
- **InfluxDB 2** (ein Messpunkt pro Monitor-Eintrag)

Mit **Verbindung testen** erkennt WebDesk eine fehlende Datenbank, Tabelle oder einen fehlenden Bucket und legt sie auf Wunsch
mit einem Klick an. Auf Wunsch wird jede erfolgreiche Speicherung im Log vermerkt (ein Datenschutzhinweis steht in den
Einstellungen).

Mit **MySQL** stehen zusätzlich der **Verlauf** und die **Suche** zur Verfügung: In jedem Direkt-Chat erscheint dann ein
Knopf 🔎 mit dem **QSO-Dialog** (Verlauf mit Filter, Textsuche) und einem Knopf 📋 für die zuletzt kontaktierten Partner.

## KI

Mit einem KI-Zugang (z. B. OpenAI oder Azure) und einer MySQL-Datenbank bekommt der QSO-Dialog zwei weitere Reiter:

| Reiter | Funktion |
|---|---|
| **KI-Zusammenfassung** | Fasst die letzten Nachrichten eines Kontakts zusammen |
| **KI-Suche** | Beantwortet eine Frage in eigenen Worten und nennt die Zeitpunkte der Fundstellen |

Ohne KI-Zugang sind diese beiden Reiter ausgegraut, Verlauf und Suche funktionieren trotzdem. Außerdem nutzt die
[Karte](07-map.md) die KI für die Beschreibung einer Station.

## Webhook

Der Webhook schickt Ereignisse als **HTTP-POST** (JSON) an eine Adresse deiner Wahl, etwa an Home Assistant, Node-RED, n8n
oder IFTTT.

- Du wählst, welche Ereignisse gesendet werden: **Nachrichten**, **Positionen** und **Telemetrie** getrennt.
- Das JSON enthält u. a. `event`, `timestamp`, `from`, `to`, `text`, `rssi`, `snr`, Position, Akku, Firmware und den
  Relay-Weg. Leere Felder fehlen.
- Der Aufruf wartet höchstens 10 Sekunden und stört den Empfang nie. Fehler werden nur protokolliert.

## MQTT

Mit **MQTT** verbindet sich WebDesk mit einem Broker (z. B. Mosquitto oder dem Mosquitto-Add-on von Home Assistant). Du
kannst Benutzername und Passwort, **TLS** und ein **Themen-Präfix** angeben (Standard `meshcom`). Bei Verbindungsverlust
verbindet sich WebDesk selbst wieder.

**Veröffentlichen:** WebDesk schickt eingehende Ereignisse an diese Themen (der Inhalt ist derselbe JSON wie beim Webhook):

| Ereignis | Thema |
|---|---|
| Rundruf (alle) | `{Präfix}/broadcast` |
| Gruppe | `{Präfix}/group/{Gruppe}` |
| Direktnachricht | `{Präfix}/dm/{Rufzeichen}` |
| Position | `{Präfix}/position/{Rufzeichen}` |
| Telemetrie | `{Präfix}/telemetry/{Rufzeichen}` |
| Ungelesen-Hinweis | `{Präfix}/unread/{Rufzeichen}` |
| Gelesen | `{Präfix}/read/{Rufzeichen}` |

**Ungelesen-Hinweis:** Er ist gedacht für eine Handy-Benachrichtigung, die nur kommt, wenn du eine Direktnachricht **nicht
selbst gesehen** hast. Kommt eine Direktnachricht an (Bot-Befehle zählen nicht) und niemand hat sich den Chat innerhalb der
**Wartezeit** (Standard 60 Sekunden) angesehen, veröffentlicht WebDesk `unread` mit der Zahl der ungesehenen Nachrichten und
dem letzten Text. „Angesehen“ heißt: Der Chat ist in einem **sichtbaren** Browser-Tab offen, die Unterhaltung ist der aktive
Tab, und es gab in den letzten 30 Sekunden (einstellbar) Maus-, Tastatur- oder Touch-Aktivität. Sobald du die Unterhaltung
später ansiehst, kommt `read`, damit die Benachrichtigung wieder verschwindet. Eine fertige Home-Assistant-Automation steht in
der
[Anleitung](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-unread-alert.md).

**Senden (optional):** WebDesk kann Themen abonnieren und deren Inhalt als Nachricht senden:

| Thema | Wirkung |
|---|---|
| `{Präfix}/send/broadcast` | Sendet an alle |
| `{Präfix}/send/group/{Gruppe}` | Sendet an eine Gruppe |
| `{Präfix}/send/dm/{Rufzeichen}` | Sendet eine Direktnachricht |

Der Inhalt ist JSON, z. B. `{ "text": "Nachricht" }`. Variablen wie `{mycall}` oder `{date}` werden vor dem Senden ersetzt.

## Weitere Wege

- **Telemetrie** von außen nach WebDesk: `POST /api/telemetry`, siehe [Automatik](06-automation.md).
- **Wetterdaten** (AWEKAS, Weather Underground): ebenfalls unter **Integrationen**, siehe [Automatik](06-automation.md).

Weiter geht es mit den weiteren Kapiteln. Bis dahin findest du Sicherheit, Sicherung und Update im Kapitel
[Schnellstart und Installation](01-install.md) und im [README](https://github.com/DH1FR/MeshcomWebDesk#readme).
