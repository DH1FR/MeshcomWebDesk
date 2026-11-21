# 🐳 Updating a Docker installation

MeshCom WebDesk in Docker is updated by replacing the **image**, not by swapping files, so the self-updater of the Windows/Linux installs does not apply. There are three ways:

| | You run | Needs |
|---|---|---|
| **A. One click** (recommended) | *Update now* on WebDesk's update page | `docker-compose.image.yml` with Watchtower |
| **B. One command** | `sudo ./scripts/update-docker.sh --image` | the published image |
| **C. One command, built from source** | `sudo ./scripts/update-docker.sh` | a git checkout of the project |

Your data (`./data`, `./logs`, `./keys`) survives every update.

---

## A. One click with Watchtower

WebDesk gets **no access to Docker**. A second container, [Watchtower](https://containrrr.dev/watchtower/), owns it. The button on `/update` asks Watchtower (HTTP API, secret token, only from this machine) to pull the newer image and recreate WebDesk.

1. Copy `.env.example` to `.env` and set the two values:
   ```
   WATCHTOWER_TOKEN=<a long secret, e.g. from: openssl rand -hex 16>
   WEBDESK_CHANNEL=latest
   ```
2. Start with the image file instead of `docker-compose.yml`:
   ```bash
   sudo docker compose -f docker-compose.image.yml up -d
   # with HTTPS (certificate in ./certs):
   sudo docker compose -f docker-compose.image.yml -f docker-compose.https.yml up -d
   ```
3. Open **/update**. It shows the installed version, the channel and whether a newer one exists. *Update now* pulls it and restarts WebDesk; the page reloads by itself.

Edit your device settings (`Meshcom__DeviceIp`, `Meshcom__MyCallsign`, …) in `docker-compose.image.yml` as in `docker-compose.yml`.

### Update channels (`WEBDESK_CHANNEL`)
- **`latest`** – the newest **release**. Recommended.
- **`dev`** – every push to the dev branch (image `:dev`, tagged with its commit). For testers; may be unstable. The update page then compares the build id of the running image with the head of the dev branch.

### Good to know
- The update page and the banner show a new version only for the channel you use.
- Watchtower updates only the WebDesk container (label `com.centurylinklabs.watchtower.enable`). Its API port is bound to `127.0.0.1`.
- The upstream project `containrrr/watchtower` is no longer developed but still works; maintained forks take the same settings (change the `image:` line).
- WebDesk has no login. *Update now* is therefore available to everyone who can open WebDesk, but it can only pull a newer image of WebDesk's own channel.

---

## B. One command (published image)

```bash
sudo ./scripts/update-docker.sh --image
```
Pulls the newest image and restarts. HTTPS is used automatically when `certs/meshcom-lan.pfx` exists (`--http` turns it off).

## C. One command (built from source)

```bash
sudo ./scripts/update-docker.sh
```
Does `git pull --ff-only`, rebuilds and restarts. It stops if you have local changes in tracked files (typically `docker-compose.yml`): keep your settings in a `docker-compose.override.yml` instead, or `git stash` first.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| Version unchanged after the update | `git log -1 --oneline`, `sudo docker ps` (uptime), `sudo docker logs meshcom-webdesk \| head` – the banner shows the version |
| "Watchtower rejected the token" | `WATCHTOWER_TOKEN` in `.env` must be identical for both containers; recreate them after changing it |
| "Watchtower is not reachable" | `sudo docker ps` – is `meshcom-watchtower` running? If port 8080 is taken (e.g. phpMyAdmin) set `WATCHTOWER_PORT=18080` in `.env` and recreate |
| Dev channel says "does not know its build" | the container was not started from the `:dev` image (e.g. built from source) |
| No new version although one was pushed | the dev image is built by GitHub after the push (a few minutes); the release image after the release |
