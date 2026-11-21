# 🐳 Docker-Installation aktualisieren

MeshCom WebDesk in Docker wird durch den Austausch des **Images** aktualisiert, nicht durch Dateien tauschen. Das Selbst-Update der Windows-/Linux-Installationen gilt hier deshalb nicht. Es gibt drei Wege:

| | Sie führen aus | Voraussetzung |
|---|---|---|
| **A. Ein Klick** (empfohlen) | *Jetzt aktualisieren* auf der Update-Seite von WebDesk | `docker-compose.image.yml` mit Watchtower |
| **B. Ein Befehl** | `sudo ./scripts/update-docker.sh --image` | das veröffentlichte Image |
| **C. Ein Befehl, aus dem Quellcode** | `sudo ./scripts/update-docker.sh` | ein Git-Checkout des Projekts |

Ihre Daten (`./data`, `./logs`, `./keys`) bleiben bei jedem Update erhalten.

---

## A. Ein Klick mit Watchtower

WebDesk bekommt **keinen Zugriff auf Docker**. Den hat ein zweiter Container, [Watchtower](https://containrrr.dev/watchtower/). Der Knopf auf `/update` bittet Watchtower (HTTP-API, geheimes Token, nur von diesem Rechner), das neuere Image zu laden und WebDesk neu zu erstellen.

1. `.env.example` nach `.env` kopieren und beide Werte setzen:
   ```
   WATCHTOWER_TOKEN=<ein langes Geheimnis, z. B. aus: openssl rand -hex 16>
   WEBDESK_CHANNEL=latest
   ```
2. Mit der Image-Datei statt `docker-compose.yml` starten:
   ```bash
   sudo docker compose -f docker-compose.image.yml up -d
   # mit HTTPS (Zertifikat in ./certs):
   sudo docker compose -f docker-compose.image.yml -f docker-compose.https.yml up -d
   ```
3. **/update** öffnen. Die Seite zeigt installierte Version, Kanal und ob eine neuere existiert. *Jetzt aktualisieren* lädt sie und startet WebDesk neu; die Seite lädt sich danach selbst neu.

Geräteeinstellungen (`Meshcom__DeviceIp`, `Meshcom__MyCallsign`, …) tragen Sie wie in `docker-compose.yml` in `docker-compose.image.yml` ein.

### Update-Kanäle (`WEBDESK_CHANNEL`)
- **`latest`** – das neueste **Release**. Empfohlen.
- **`dev`** – jeder Push auf den dev-Zweig (Image `:dev`, mit seinem Commit versehen). Für Tester, kann instabil sein. Die Update-Seite vergleicht dann die Build-ID des laufenden Images mit dem Kopf von dev.

### Gut zu wissen
- Update-Seite und Banner zeigen eine neue Version nur für den Kanal, den Sie nutzen.
- Watchtower aktualisiert nur den WebDesk-Container (Label `com.centurylinklabs.watchtower.enable`). Sein API-Port ist an `127.0.0.1` gebunden.
- Das Originalprojekt `containrrr/watchtower` wird nicht mehr weiterentwickelt, funktioniert aber; gepflegte Forks nehmen dieselben Einstellungen (Zeile `image:` ändern).
- WebDesk hat keine Anmeldung. *Jetzt aktualisieren* kann daher jeder auslösen, der WebDesk öffnen kann, es zieht aber nur ein neueres Image des eigenen Kanals.

---

## B. Ein Befehl (veröffentlichtes Image)

```bash
sudo ./scripts/update-docker.sh --image
```
Lädt das neueste Image und startet neu. HTTPS wird automatisch genutzt, wenn `certs/meshcom-lan.pfx` existiert (`--http` schaltet es ab).

## C. Ein Befehl (aus dem Quellcode)

```bash
sudo ./scripts/update-docker.sh
```
Macht `git pull --ff-only`, baut neu und startet neu. Bei lokalen Änderungen an versionierten Dateien (meist `docker-compose.yml`) bricht es ab: Legen Sie Ihre Einstellungen besser in eine `docker-compose.override.yml`, oder `git stash` vorher.

---

## Fehlersuche

| Symptom | Prüfen |
|---|---|
| Version nach dem Update unverändert | `git log -1 --oneline`, `sudo docker ps` (Laufzeit), `sudo docker logs meshcom-webdesk \| head` – das Banner zeigt die Version |
| „Watchtower hat das Token abgelehnt“ | `WATCHTOWER_TOKEN` in `.env` muss für beide Container gleich sein; nach einer Änderung neu erstellen |
| „Watchtower ist nicht erreichbar“ | `sudo docker ps` – läuft `meshcom-watchtower`? WebDesk muss im Host-Netzwerk laufen (`127.0.0.1:8080`) |
| Dev-Kanal meldet „kennt seinen Build nicht“ | der Container wurde nicht aus dem `:dev`-Image gestartet (z. B. aus dem Quellcode gebaut) |
| Keine neue Version, obwohl gepusht | das dev-Image baut GitHub nach dem Push (einige Minuten); das Release-Image nach dem Release |
