# Benachrichtigungen

WebDesk meldet sich auf mehreren Wegen, wenn etwas Wichtiges passiert. Fast alles lässt sich einzeln ein- und
ausschalten.

## Ton und Sprache

- Bei einer neuen **Direktnachricht an dein Rufzeichen** ertönt ein Signalton. Der Schalter **🔔 / 🔕** in der
  Statusleiste schaltet alle Töne stumm.
- Auf Wunsch liest der Browser Direktnachrichten **vor** (Sprachausgabe des Browsers, keine zusätzliche Datei nötig). Den
  Schalter **🔊 / 🔇** findest du ebenfalls in der Statusleiste. Die Sprache der Ansage folgt der Sprache der Oberfläche.

Beide Schalter merkt sich dein Browser.

## Watchlist: Rufzeichen beobachten

Unter **Einstellungen → Chat & Anzeige → Watchlist** trägst du Rufzeichen ein, die du im Auge behalten willst. WebDesk
meldet, sobald eines davon gehört wird.

- **Ohne SSID** (z. B. `DH1FR`) gilt für alle SSIDs (`DH1FR`, `DH1FR-1`, `DH1FR-11` …). **Mit SSID** (z. B. `DH1FR-1`)
  nur für genau dieses Rufzeichen.
- Bei einem Treffer ertönt ein eigener Dreiklang, und oben rechts erscheint eine **Meldung** (Toast) mit Rufzeichen, Art
  des Pakets (`MSG`, `POS`, `TEL`, `ACK`) und Alter. Mehrere Treffer stehen in derselben Meldung. Die Meldung
  verschwindet nach einer einstellbaren Zeit (Standard 5 Minuten) oder mit ✕.
- Du wählst, **für welche Pakete** gemeldet wird: Nachrichten und Positionen sind meist sinnvoll, Telemetrie und
  Bestätigungen sind standardmäßig aus, weil sie oft und regelmäßig kommen.
- Die **Gruppenliste** (z. B. `26261`) meldet jede neue Textnachricht in dieser Gruppe mit 👥-Meldung, Ton und Ansage.
  Eigene Nachrichten zählen nicht, und die Ansage gibt es höchstens alle 5 Minuten pro Gruppe (sonst nur den Ton).
- Änderungen gelten sofort, ohne Neustart.

## CQ-Rufe

WebDesk erkennt **CQ-Rufe** in Gruppen und auch **CQ an alle** (landet im Tab **All**). Es erscheint eine gelbe Meldung
**CQ** mit Rufzeichen und Gruppe (Standard 60 Sekunden sichtbar), dazu ein CQ-Ton in Morsezeichen und, wenn die
Sprachausgabe an ist, eine Ansage wie „C Q C Q von … in Gruppe 262“. Eigene Rufe und Wörter wie „FREQUENCY“ lösen nichts
aus. Der Schalter **CQ-Rufe an Alle** steht in derselben Watchlist-Karte (standardmäßig an).

## Erwähnungen mit `@`

Schreibt dir jemand in einer **Gruppe** oder an **alle** eine Nachricht mit `@<dein Rufzeichen>` (z. B. `@DH1FR-4`),
erkennt WebDesk das:

- Die Zeile wird kräftig eingefärbt, mit einem Streifen am Rand und der Marke **@ für dich**. Das Rufzeichen im Text ist
  hervorgehoben, und am Tab erscheint ein Zähler **@N**.
- Zusätzlich gibt es eine Meldung **Du wurdest erwähnt** (ein Klick öffnet den Tab), einen eigenen Ton und eine Ansage,
  höchstens einmal pro Minute und Tab.
- Es zählt das ganze Rufzeichen ohne Rücksicht auf Groß- und Kleinschreibung: `@DH1FR-4` gilt für dein `DH1FR-4`,
  `@DH1FR` ohne SSID für jedes Rufzeichen mit dieser Basis. Andere SSIDs, E-Mail-Adressen und fremde Rufzeichen zählen
  nicht.
- Den Hinweiston schaltest du unter **Watchlist → Erwähnung** (standardmäßig an). Die Markierung der Zeile und der Zähler
  am Tab bleiben immer.

Dein eigenes Rufzeichen ist in jeder Nachricht hervorgehoben, auch in alten.

## Ungelesen im Browser-Tab

Solange Nachrichten ungelesen sind, steht im Titel des Browser-Tabs die Zahl, z. B. `(3) MeshCom WebDesk`, und das
Symbol hat einen roten Punkt. Bei einer installierten App steht die Zahl auch auf dem Symbol (Taskleiste oder Dock).
Das funktioniert, wenn der Ton aus ist. Gezählt werden Direktnachrichten, Gruppen und **All** aller Nodes. Der Schalter
heißt **Ungelesene im Browser-Tab anzeigen** (**Einstellungen → Chat & Anzeige**).

## Desktop-Benachrichtigungen

Wenn niemand auf WebDesk schaut, kann WebDesk Hinweise des Betriebssystems (Pop-ups) senden, für Direktnachrichten,
Erwähnungen, Watchlist-Rufzeichen, beobachtete Gruppen und CQ-Rufe. Solange du WebDesk ansiehst, gibt es kein Pop-up, damit
nichts doppelt gemeldet wird. Ein Klick darauf bringt das Fenster nach vorn und öffnet den passenden Tab.

Das ist **standardmäßig aus**. Du schaltest es unter **Watchlist → Desktop-Benachrichtigungen** ein, der Browser fragt
einmal nach der Erlaubnis. Dort wählst du auch, welche Arten du bekommen willst, und kannst sie mit einem Knopf testen.
Voraussetzung ist eine sichere Verbindung (HTTPS oder `localhost`). Ein Handy mit gesperrtem Bildschirm oder geschlossener
App bekommt nichts. Dafür gibt es die Möglichkeit unten.

## Handy-Benachrichtigung bei ungesehenen Nachrichten (MQTT)

Wenn du eine Direktnachricht nicht selbst gesehen hast, kann WebDesk das über **MQTT** melden, zum Beispiel für eine
Benachrichtigung in Home Assistant auf dem Handy. Wie das eingerichtet wird, steht in
[Integrationen](08-integrations.md) und in der
[Anleitung zum Home-Assistant-Alarm](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/homeassistant-unread-alert.md).

Weiter mit [Automatik](06-automation.md).
