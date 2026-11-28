# Sicherheit und Zugriff

Standardmäßig ist WebDesk für **jeden** erreichbar, der die Adresse im Netz kennt: Wer sie öffnet, kann Nachrichten lesen,
senden und Einstellungen ändern. Dieses Kapitel zeigt, wie du das absicherst und wie du WebDesk auf dem Handy nutzt.

## Zugriffsschutz mit Passwort

Unter **Einstellungen → System → Zugriffsschutz** richtest du ein gemeinsames Passwort ein.

| Modus | Wirkung |
|---|---|
| **Aus** (Standard) | Alles wie bisher, kein Passwort |
| **Nur von anderen Geräten** (empfohlen) | Der Rechner, auf dem WebDesk läuft, kommt ohne Passwort hinein. Handys, Tablets und andere Rechner melden sich **einmal pro Gerät** an und bleiben **90 Tage** angemeldet |
| **Immer** | Auch dieser Rechner braucht das Passwort |

So gehst du vor: Modus wählen, das Passwort **zweimal** eingeben und **Übernehmen** drücken. Es gilt sofort, und dieser
Browser ist gleich angemeldet. Dort gibt es auch **Alle Geräte abmelden** und **Dieses Gerät abmelden**.

Was du wissen solltest:

- Das Passwort wird nur als **gesalzener Hash** (PBKDF2) in `<DataPath>/access.json` gespeichert. Es ist nicht Teil der
  Einstellungs-Sicherung, und **Speichern** ändert es nicht.
- Falsche Eingaben werden je Adresse gebremst (5 Versuche, dann 30 Sekunden, dann länger bis höchstens 5 Minuten).
- Eine kleine eigene **Anmeldeseite** (`/login`) erscheint in der Sprache der Oberfläche. Alles andere, auch Seiten, die
  Live-Verbindung und die `/api/*`-Schnittstellen, braucht die Anmeldung. Nur `POST /api/telemetry` behält seinen eigenen
  Schlüssel (`X-Api-Key`), damit Home Assistant & Co. weiter senden können.
- **Passwort vergessen?** Starte WebDesk einmal mit `--reset-password`. Das schaltet den Schutz aus.
  `--reset-password NEUESPASSWORT` setzt ein neues Passwort (Achtung: Ein Passwort auf der Kommandozeile landet im
  Verlauf deiner Shell). Unter Docker: `docker exec <container> ./MeshcomWebDesk --reset-password`. Ohne Zugriff auf eine Shell startest du
  den Container einmal mit der Umgebungsvariable `MESHCOM_RESET_ACCESS=1` und entfernst sie danach wieder. Ein laufendes
  WebDesk übernimmt die Änderung nach wenigen Sekunden.
- **Docker:** Mit `network_mode: host` (wie in `docker-compose.image.yml`) zählt ein Browser auf dem Docker-Rechner selbst als
  `localhost`. Andere Geräte brauchen das Passwort.

> ⚠️ Nutze nach Möglichkeit **HTTPS** (nächster Abschnitt). Über einfaches HTTP wird das Passwort unverschlüsselt durch das
> Netz geschickt. Wenn WebDesk auch auf HTTPS lauscht, leitet es Anfragen von anderen Geräten dorthin um. Sonst warnt die
> Anmeldeseite, dass die Verbindung nicht verschlüsselt ist.

## HTTPS im Heimnetz

HTTPS verschlüsselt die Verbindung und ist nötig, damit sich WebDesk auf Handy und Tablet als **App installieren** lässt.
Im Heimnetz nutzt du dafür ein **selbstsigniertes Zertifikat**, das für die IP-Adresse deines Rechners ausgestellt wird.

**1. Zertifikat erstellen** (einmalig):

Windows (PowerShell als Administrator, im Ordner der Anwendung):

```powershell
.\scripts\create-lan-cert.ps1                       # erkennt die IP selbst
.\scripts\create-lan-cert.ps1 -LanIp 192.168.1.100  # oder IP angeben
```

Linux (mit `openssl`):

```bash
chmod +x scripts/create-lan-cert.sh
./scripts/create-lan-cert.sh                        # oder mit IP: ./scripts/create-lan-cert.sh 192.168.1.100
```

Das Skript erzeugt `meshcom-lan.pfx` (für WebDesk) und `meshcom-lan.crt` (für Handys und Tablets). Unter Windows trägt es das
Zertifikat gleich als vertrauenswürdig ein, damit der PC keine Warnung zeigt.

**2. WebDesk mit HTTPS starten:** Danach lauscht WebDesk zusätzlich auf Port **5163** (HTTP auf **5162** bleibt bestehen).

- Direkt gestartet: mit dem Startprofil `lan-https` (`dotnet run --launch-profile lan-https`)
- Docker: mit der zusätzlichen Datei `docker-compose.https.yml`

**3. Auf dem Handy vertrauen:** Kopiere `meshcom-lan.crt` auf das Gerät und installiere es als vertrauenswürdiges
Zertifikat. Dann öffnest du `https://<IP-des-Rechners>:5163`.

Die genauen Schritte, auch für Docker, stehen im
[README](https://github.com/DH1FR/MeshcomWebDesk#readme) im Abschnitt „HTTPS & Zertifikat“.

## Als App installieren (PWA)

Über HTTPS lässt sich WebDesk wie eine App installieren: mit eigenem Symbol, ohne Adressleiste und mit Schnellzugriffen auf
**Chat** und **Karte**. Eine Installation ohne Store, ohne Download.

- **iPhone / iPad (Safari):** `https://<IP>:5163` öffnen, Teilen-Symbol antippen, **Zum Home-Bildschirm**, Namen
  bestätigen.
- **Android (Chrome):** `https://<IP>:5163` öffnen, Menü ⋮ → **App installieren** (oder dem Banner folgen).
- **Windows / Mac (Chrome oder Edge):** in der Adressleiste auf **⊕ Installieren** klicken oder Menü → **MeshCom WebDesk
  installieren**.

> ⚠️ **Offline geht nicht:** WebDesk braucht immer die Verbindung zum Server, auf dem es läuft. Die App-Installation ändert
> nur die Darstellung, nicht den Betrieb.

## Handy öffnen: QR-Code

Der Knopf **QR** in der Menüleiste zeigt einen QR-Code mit der Adresse von WebDesk. Du scannst ihn mit dem Handy und
öffnest die Oberfläche. Der Dialog weist darauf hin, wenn **kein Zugriffsschutz** eingerichtet ist.

Weiter mit [Sicherung, Wartung und Fehlersuche](10-maintenance.md).
