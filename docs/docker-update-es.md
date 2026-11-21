# 🐳 Actualizar una instalación Docker

MeshCom WebDesk en Docker se actualiza sustituyendo la **imagen**, no intercambiando archivos, por lo que el autoactualizador de las instalaciones Windows/Linux no se aplica. Hay tres vías:

| | Ejecutas | Requiere |
|---|---|---|
| **A. Un clic** (recomendado) | *Actualizar ahora* en la página de actualización de WebDesk | `docker-compose.image.yml` con Watchtower |
| **B. Un comando** | `sudo ./scripts/update-docker.sh --image` | la imagen publicada |
| **C. Un comando, desde el código fuente** | `sudo ./scripts/update-docker.sh` | un checkout git del proyecto |

Tus datos (`./data`, `./logs`, `./keys`) se conservan en cada actualización.

---

## A. Un clic con Watchtower

WebDesk **no tiene acceso a Docker**. Lo tiene un segundo contenedor, [Watchtower](https://containrrr.dev/watchtower/). El botón de `/update` le pide a Watchtower (API HTTP, token secreto, solo desde este equipo) que descargue la imagen nueva y vuelva a crear WebDesk.

1. Copia `.env.example` a `.env` y define los dos valores:
   ```
   WATCHTOWER_TOKEN=<un secreto largo, p. ej. de: openssl rand -hex 16>
   WEBDESK_CHANNEL=latest
   ```
2. Inicia con el archivo de imagen en lugar de `docker-compose.yml`:
   ```bash
   sudo docker compose -f docker-compose.image.yml up -d
   # con HTTPS (certificado en ./certs):
   sudo docker compose -f docker-compose.image.yml -f docker-compose.https.yml up -d
   ```
3. Abre **/update**. Muestra la versión instalada, el canal y si hay una más reciente. *Actualizar ahora* la descarga y reinicia WebDesk; la página se recarga sola.

Los ajustes del dispositivo (`Meshcom__DeviceIp`, `Meshcom__MyCallsign`, …) se introducen en `docker-compose.image.yml` como en `docker-compose.yml`.

### Canales de actualización (`WEBDESK_CHANNEL`)
- **`latest`** – el **release** más reciente. Recomendado.
- **`dev`** – cada push a la rama dev (imagen `:dev`, marcada con su commit). Para probadores; puede ser inestable.

### Conviene saber
- La página de actualización y el aviso solo muestran una versión nueva del canal que usas.
- Watchtower solo actualiza el contenedor de WebDesk. Su puerto de API está ligado a `127.0.0.1`.
- El original `containrrr/watchtower` ya no funciona con Docker actual (Docker 29: «client version 1.25 is too old»), por lo que el archivo usa el fork mantenido `nickfedor/watchtower` con los mismos ajustes. Es una imagen de terceros con acceso al socket de Docker; puedes cambiar la línea `image:` por otro fork.
- WebDesk no tiene inicio de sesión: *Actualizar ahora* puede activarlo cualquiera que abra WebDesk, pero solo descarga una imagen más nueva de su propio canal.

---

## B. Un comando (imagen publicada)

```bash
sudo ./scripts/update-docker.sh --image
```

## C. Un comando (desde el código fuente)

```bash
sudo ./scripts/update-docker.sh
```
Hace `git pull --ff-only`, recompila y reinicia. Se detiene si tienes cambios locales en archivos versionados (normalmente `docker-compose.yml`): guarda tus ajustes en un `docker-compose.override.yml` o haz `git stash` antes.

---

## Solución de problemas

| Síntoma | Comprobar |
|---|---|
| La versión no cambia tras actualizar | `git log -1 --oneline`, `sudo docker ps`, `sudo docker logs meshcom-webdesk \| head` |
| «Watchtower rechazó el token» | `WATCHTOWER_TOKEN` en `.env` debe ser igual para ambos contenedores |
| «Watchtower no es accesible» | ¿Está en marcha `meshcom-watchtower`? Si el puerto 8080 está ocupado, define `WATCHTOWER_PORT=18080` en `.env` y recrea |
