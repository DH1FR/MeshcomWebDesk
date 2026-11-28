# Die Oberfläche

Oben in der Menüleiste erreichst du alle Bereiche von WebDesk:

| Eintrag | Inhalt |
|---|---|
| **Chat** | Unterhaltungen (Tabs) mit dem Monitor darunter |
| **MH** | Liste der zuletzt gehörten Stationen |
| **Karte** | Live-Karte der Stationen |
| **Einstellungen** | Alle Einstellungen im Browser |
| **Löschen** | Löscht alle gespeicherten Chats, Monitor-Einträge und die MH-Liste (mit Rückfrage) |
| **Über** | Version, Links, Aktualisieren und Sicherungen |
| **QR** | QR-Code, um WebDesk auf Handy oder Tablet zu öffnen |
| **Hilfe** | Dieses Handbuch |

Neben dem Namen steht die Versionsnummer. Verwendest du mehrere Nodes, wählst du den aktiven Node im Node-Umschalter
der Kopfzeile.

## Chat

### Tabs

Jede Unterhaltung hat einen **eigenen Tab**:

- **Direktnachrichten** – für jedes Rufzeichen entsteht automatisch ein Tab
- **Gruppen** – Gruppen erscheinen als `#<Gruppe>`-Tabs. Für Gruppennummern kannst du in den Einstellungen Namen
  vergeben (z. B. `#262` → `DL`)
- **All** – Nachrichten an alle (`*` oder `CQCQCQ`)

Mit **+** öffnest du einen neuen Tab, die Tabs lassen sich per **Drag & Drop** umsortieren (am Handy durch langes
Drücken). Die Reihenfolge bleibt in deinem Browser gespeichert. Sind sehr viele Tabs offen, schaltet die Leiste auf eine
einzeilige Ansicht, die du seitlich wischst.

Ein Klick auf ein **Rufzeichen** in einer Nachricht oder im Monitor öffnet den Tab dieser Station. Beim Überfahren
erscheint ein kleiner **@**-Knopf, der `@RUFZEICHEN` in das Eingabefeld einfügt, praktisch für Antworten in Gruppen.

### Gelesen und ungelesen

- Tabs mit neuen Nachrichten zeigen einen **gelben Zähler**.
- Öffnest du einen Tab mit vielen ungelesenen Nachrichten, beginnt die Ansicht bei der Trennlinie **Neue Nachrichten**.
  Ein Knopf **↑ N ungelesen** springt dorthin zurück.
- Die Liste läuft nur mit, solange du am Ende bist. Scrollst du nach oben, bleibt die Position stehen, und **↓ N Neue
  Nachrichten** zeigt, wie viele inzwischen eingetroffen sind.
- Im Browser-Titel steht die Zahl der ungelesenen Nachrichten (`(3) MeshCom WebDesk`) und das Symbol bekommt einen
  roten Punkt. Als gelesen zählt, was du wirklich angesehen hast: Der Tab muss sichtbar und aktiv sein.

### Zustellanzeige

Hinter jeder gesendeten Nachricht zeigt ein Symbol, wie weit sie gekommen ist:

| Symbol | Bedeutung |
|---|---|
| ⏳ grau | Wartet auf das Echo des Nodes (in der Warteschlange) |
| ✓ blau | Der Node hat über LoRa gesendet |
| ✓✓ grün | Der Empfänger hat den Empfang bestätigt |
| ☁️ | An Gruppe oder alle gesendet (keine Bestätigung zu erwarten) |
| ☁️✓ | Über ein Gateway zugestellt |
| ❌ rot | Zustellung fehlgeschlagen, der Node hat nach mehreren Wiederholungen aufgegeben (Firmware 4.40a) |
| 📬 | Von einem Mailbox-Node zurückgehalten, bis der Empfänger erreichbar ist |

## MH – zuletzt gehörte Stationen

Die Seite **MH** listet alle Stationen, die der Node gehört hat, mit

- **Position** (aus GPS-Paketen), **QTH-Locator** und **Entfernung** zu deiner Station
- **Signalstärke** (RSSI und SNR, farbig: grün, gelb, rot)
- **Akkustand** 🔋 (grün über 60 %, gelb über 30 %, rot darunter)
- Firmware und Hardware (als Hinweis beim Überfahren des Rufzeichens)

Beim Überfahren (am Handy: Antippen) eines Rufzeichens erscheint eine **Stationskarte** mit den Details und den Knöpfen
**💬 Chat öffnen** und **🔗 aprs.fi**. Das Suchfeld 🔍 oben filtert nach Rufzeichen, Nachrichtentext oder Name.
Ist die QRZ.com-Anbindung eingeschaltet, zeigt WebDesk zusätzlich Vorname und Wohnort des Funkamateurs.

## Monitor

Unter dem Chat zeigt der **Monitor** alles, was der Node meldet, mit dem neuesten Eintrag oben:

- **Typ**: `MSG` Nachricht, `POS` Position, `TEL` Telemetrie, `ACK` Bestätigung, `SYS` System
- **Richtung**: `RX` empfangen, `TX` gesendet
- Bei weitergeleiteten Nachrichten der **vollständige Weg** über die Relais, z. B. `OE1XAR-62 ⟶ DL0VBK-12 → alle`
- Farben: grün für gesendet, cyan für Positionen, violett für Telemetrie, gold für Bestätigungen

Mit dem **Trennbalken** stellst du das Verhältnis von Chat und Monitor ein, mit **▼/▲** blendest du den Monitor aus oder
ein. Beides merkt sich dein Browser. Das Suchfeld 🔍 in der Titelleiste des Monitors zeigt nur Zeilen mit einem
Rufzeichen oder Textstück.

## Statusleiste

Die Leiste am unteren Rand zeigt auf einen Blick:

- den **UDP-Status** (🔴 kein Socket, 🟡 warte auf Signal, 🟢 Empfang OK, siehe [Node verbinden](02-connect-node.md))
- den letzten Empfang mit Absender und Signalstärke
- die Zahl der gesendeten Nachrichten, dein Rufzeichen und die Adresse des Nodes
- deine eigene Position (vom Node oder aus dem Browser-GPS)
- die Schalter **🔔 / 🔕** für den Signalton und **🔊 / 🔇** für die Sprachansage

Bei einer neuen Direktnachricht ertönt ein Signalton, und auf Wunsch liest der Browser sie vor. Welche weiteren
Hinweise (Erwähnungen, CQ-Rufe, Desktop-Benachrichtigungen) es gibt, steht in einem späteren Kapitel.

## Am Handy

WebDesk lässt sich im Browser des Handys öffnen (QR-Code im Menü) und als App installieren. Bei kleinen Bildschirmen und
beim Drehen des Handys wechselt die Ansicht automatisch auf eine kompakte Darstellung mit einzeiliger Tab-Leiste.

Weiter mit [Nachrichten schreiben](04-writing-messages.md).
