# Nachrichten schreiben

Unten im Chat steht die **Sendeleiste**. Du wählst oben den Tab des Empfängers (Rufzeichen, Gruppe oder **All**), tippst
die Nachricht und sendest sie mit **Senden** oder der Eingabetaste.

## Länge der Nachricht

Eine LoRa-Nachricht darf höchstens **149 Zeichen** lang sein. Neben dem Eingabefeld zählt ein Zähler **X/149** mit:
grau, ab 130 gelb und ab 145 rot und fett. Längeres Tippen lässt das Feld nicht zu. Antworten des Bots und Baken-Texte,
die länger sind, teilt WebDesk selbst auf mehrere Pakete auf (mit zwei Sekunden Pause dazwischen).

## Variablen: `{}`

In Texten kannst du **Variablen** in geschweiften Klammern verwenden, die WebDesk beim Senden durch den aktuellen Wert
ersetzt. Der Knopf **`{}`** in der Sendeleiste öffnet eine scrollbare Tabelle mit allen Variablen und einer kurzen
Beschreibung. Ein Klick fügt die Variable an der Cursor-Position ein. Werte, die etwas länger zum Ermitteln brauchen,
werden erst beim Öffnen geladen.

Du kannst Variablen auch direkt tippen, zum Beispiel `{date}`. Mit der Taste **Tab** siehst du vorab, was daraus wird.
Beim Senden werden alle Variablen in jedem Fall ersetzt.

| Variable | Wert |
|---|---|
| `{mycall}` | Eigenes Rufzeichen |
| `{callsign}` | Rufzeichen des Gesprächspartners |
| `{dest-name}` / `{dest-loc}` | Vorname / Standort des Partners (aus QRZ.com) |
| `{mylocator}` / `{locator}` | Eigener QTH-Locator / QTH-Locator des Partners |
| `{rssi}` / `{snr}` | Signalstärke des letzten Empfangs |
| `{hw}` | Hardware-Typ des Partners |
| `{route}` / `{hops}` | Relay-Weg / Anzahl der Relay-Hops |
| `{date}` / `{time}` | Aktuelles Datum / Uhrzeit |
| `{last-qso}` | Datum und Uhrzeit des **vorigen** Kontakts mit diesem Rufzeichen (`kein QSO`, wenn es keinen gab) |
| `{telemetry}` | Telemetrie-Text |
| `{node-firmware}` / `{node-hw}` | Firmware / Hardware deines Nodes |
| `{version}` | Version von WebDesk |
| `{my-tx-power}`, `{my-eirp}`, `{my-antenna}`, `{my-antenna-type}`, `{my-antenna-height}`, `{my-freq}` | Angaben zu deiner Station (aus den Einstellungen) |

Dieselben Variablen gelten auch in Schnelltexten, Auto-Reply, Bot-Befehlen und Baken. Welche Werte WebDesk hat, hängt
davon ab, was bekannt ist: Ohne QRZ.com-Anbindung gibt es zum Beispiel keinen Vornamen des Partners.

## Schnelltexte ⚡

Schnelltexte sind Knöpfe in der Sendeleiste mit vorbereiteten Texten. Ein Klick **lädt den Text in das Eingabefeld**.
Du kannst ihn vor dem Senden noch anpassen. Variablen sind erlaubt.

- Die Knöpfe richtest du unter **Einstellungen → Schnelltexte** ein. Im Menü ⚡ lassen sie sich per **Drag & Drop**
  umsortieren; die neue Reihenfolge wird sofort gespeichert.
- Mit **Export** und **Import** sicherst du die Liste in einer Datei (`MeshComWebDesk-quick-texts.json`) oder gibst sie an
  andere Funkamateure weiter. Ein Import ersetzt die vorhandene Liste.

## Emoticons werden zu Emojis

Texte wie `;-)`, `:D` oder `<3` wandelt WebDesk in Emojis um: schon beim Tippen, sobald du ein Leerzeichen setzt, und beim
Senden für das letzte Wort. Das ist standardmäßig an und lässt sich unter **Einstellungen → Chat & Anzeige → Chat & Monitor** mit
**Emoticons in Emojis umwandeln** ausschalten. Es funktioniert auch auf der iPhone-Tastatur.

## Andere ansprechen

- Mit dem **@**-Knopf neben einem Rufzeichen (im Chat oder im Monitor) fügst du `@RUFZEICHEN` an der Cursor-Position ein.
  Das ist praktisch, wenn du in einer Gruppe jemanden ansprechen willst.
- Ein Klick auf ein Rufzeichen öffnet den Tab dieser Station, ohne dass du suchen musst.
- Mit **+** öffnest du einen Tab für ein Rufzeichen oder eine Gruppe, z. B. `OE1KBC`, `#gruppe` oder `*`.

## Nachricht kopieren

Fährst du in einem Direkt-Chat über eine Nachricht, erscheint ein Knopf 📋. Er kopiert den Text in die Zwischenablage.

## Rechtschreibung

Das Eingabefeld nutzt die **Rechtschreibprüfung deines Browsers**. Falsch geschriebene Wörter werden unterstrichen, wenn
die Prüfung in den Browser-Einstellungen eingeschaltet ist.

Weiter mit [Benachrichtigungen](05-notifications.md).
