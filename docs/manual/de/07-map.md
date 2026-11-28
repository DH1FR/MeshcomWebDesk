# Karte und Reichweite

Die Seite **Karte** zeigt alle Stationen, deren Position WebDesk kennt, auf einer **OpenStreetMap**-Karte. Sie
aktualisiert sich laufend, sobald neue Positionspakete eintreffen.

## Marker

- Jede Station ist ein **Punkt mit Rufzeichen**. Die Farbe zeigt die Signalstärke: 🟢 besser als −90 dBm, 🟡 besser als
  −105 dBm, 🔴 schwächer.
- **Gateways** erscheinen auf der Karte und in der MH-Liste mit einem 🌐-Symbol und eigener Farbe. WebDesk holt die Liste der
  Gateways alle 15 Minuten von den MeshCom-Dashboards (OE, DL, IT, HB, SP). An- und ausschalten und die Quelle wählen
  kannst du unter **Einstellungen → Chat & Anzeige → Chat & Monitor** (**Gateway-Hervorhebung**, **Gateway-Quelle**).
- Deine **eigene Position** erscheint als goldene Raute ◆.
- Ein Klick auf einen Marker öffnet ein **Fenster** mit Rufzeichen, Vorname und Ort (wenn die QRZ.com-Anbindung an ist),
  letzter Nachricht, Signalstärke, Akkustand, Höhe, QTH-Locator, Koordinaten (Link zu OpenStreetMap) und einem Link zu
  **aprs.fi**.
- Hat der Node Telemetrie gesendet, zeigt das Fenster auch Temperatur, Luftdruck und Feuchte.

## Suchen und Merken

- Das Suchfeld 🔍 springt zu einer Station und öffnet ihr Fenster. Bei mehreren Treffern erscheint eine Auswahl, bei keinem
  Treffer eine Meldung.
- Beim **ersten Öffnen** zoomt die Karte auf einen Umkreis von **50 km** um deine Station, sobald deine Position bekannt ist.
- Position und Zoom der Karte merkt sich dein Browser und stellt sie beim nächsten Besuch wieder her.
- Eine kompakte Zeile unten zeigt die Zahl der Stationen und dein Rufzeichen.

## Reichweite 📶

Mit dem Knopf **📶** in der Leiste der Karte blendest du die **Reichweite** ein:

- Das **blaue Polygon** ist die **gemessene Reichweite**. Es umschließt alle Stationen, die direkt (ohne Relais) gehört wurden,
  und deine eigene Position.
- Der **gelbe Kreis** ist die **theoretische Reichweite im freien Raum**. Er erscheint, wenn du unter **Station / HF-Parameter**
  Leistung, Antenne, Frequenz und Systemreserve eingegeben hast (siehe [Automatik](06-automation.md)).
- Die Legende unterscheidet beide Flächen. Die Berechnung läuft jedes Mal neu, wenn du den Knopf umschaltest.

## KI-Beschreibung einer Station 🤖

Ist die KI-Anbindung eingerichtet (siehe [Integrationen](08-integrations.md)), hat das Fenster jeder Station einen Knopf 🤖. Er
schickt die Daten der Station (Rufzeichen, Signal, Akku, Firmware, Weg, QRZ-Angaben) an die KI und zeigt eine kurze
Beschreibung direkt im Fenster.

Weiter mit [Integrationen](08-integrations.md).
