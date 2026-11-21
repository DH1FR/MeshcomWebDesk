# 🐳 Aggiornare un'installazione Docker

MeshCom WebDesk in Docker si aggiorna sostituendo l'**immagine**, non scambiando file: l'autoaggiornamento delle installazioni Windows/Linux non si applica. Ci sono tre vie:

| | Esegui | Richiede |
|---|---|---|
| **A. Un clic** (consigliato) | *Aggiorna ora* nella pagina di aggiornamento di WebDesk | `docker-compose.image.yml` con Watchtower |
| **B. Un comando** | `sudo ./scripts/update-docker.sh --image` | l'immagine pubblicata |
| **C. Un comando, dal sorgente** | `sudo ./scripts/update-docker.sh` | un checkout git del progetto |

I tuoi dati (`./data`, `./logs`, `./keys`) restano a ogni aggiornamento.

---

## A. Un clic con Watchtower

WebDesk **non ha accesso a Docker**. Ce l'ha un secondo container, [Watchtower](https://containrrr.dev/watchtower/). Il pulsante in `/update` chiede a Watchtower (API HTTP, token segreto, solo da questo computer) di scaricare la nuova immagine e ricreare WebDesk.

1. Copia `.env.example` in `.env` e imposta i due valori:
   ```
   WATCHTOWER_TOKEN=<un segreto lungo, ad es. da: openssl rand -hex 16>
   WEBDESK_CHANNEL=latest
   ```
2. Avvia con il file immagine al posto di `docker-compose.yml`:
   ```bash
   sudo docker compose -f docker-compose.image.yml up -d
   # con HTTPS (certificato in ./certs):
   sudo docker compose -f docker-compose.image.yml -f docker-compose.https.yml up -d
   ```
3. Apri **/update**. Mostra la versione installata, il canale e se ne esiste una più recente. *Aggiorna ora* la scarica e riavvia WebDesk; la pagina si ricarica da sola.

Le impostazioni del dispositivo (`Meshcom__DeviceIp`, `Meshcom__MyCallsign`, …) si inseriscono in `docker-compose.image.yml` come in `docker-compose.yml`.

### Canali di aggiornamento (`WEBDESK_CHANNEL`)
- **`latest`** – il **release** più recente. Consigliato.
- **`dev`** – ogni push sul ramo dev (immagine `:dev`, contrassegnata con il suo commit). Per i tester; può essere instabile.

### Da sapere
- La pagina di aggiornamento e l'avviso mostrano una nuova versione solo per il canale che usi.
- Watchtower aggiorna solo il container di WebDesk. La sua porta API è legata a `127.0.0.1`.
- Il progetto originale `containrrr/watchtower` non è più sviluppato ma funziona; i fork mantenuti accettano le stesse impostazioni (cambia la riga `image:`).
- WebDesk non ha login: *Aggiorna ora* può attivarlo chiunque apra WebDesk, ma scarica solo un'immagine più nuova del proprio canale.

---

## B. Un comando (immagine pubblicata)

```bash
sudo ./scripts/update-docker.sh --image
```

## C. Un comando (dal sorgente)

```bash
sudo ./scripts/update-docker.sh
```
Esegue `git pull --ff-only`, ricompila e riavvia. Si ferma se hai modifiche locali a file versionati (di solito `docker-compose.yml`): metti le tue impostazioni in un `docker-compose.override.yml` oppure esegui prima `git stash`.

---

## Risoluzione dei problemi

| Sintomo | Controllare |
|---|---|
| La versione non cambia dopo l'aggiornamento | `git log -1 --oneline`, `sudo docker ps`, `sudo docker logs meshcom-webdesk \| head` |
| «Watchtower ha rifiutato il token» | `WATCHTOWER_TOKEN` in `.env` deve essere uguale per entrambi i container |
| «Watchtower non è raggiungibile» | `meshcom-watchtower` è in esecuzione? Se la porta 8080 è occupata, imposta `WATCHTOWER_PORT=18080` in `.env` e ricrea |
