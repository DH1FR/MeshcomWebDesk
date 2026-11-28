# Security and access

By default WebDesk is open to **everybody** who knows the address in the network: whoever opens it can read and send messages
and change settings. This chapter shows how to protect it and how to use WebDesk on a phone.

## Access protection with a password

Under **Settings → System → Access protection** you set up one shared password.

| Mode | Effect |
|---|---|
| **Off** (default) | Everything as before, no password |
| **Only from other devices** (recommended) | The computer running WebDesk gets in without a password. Phones, tablets and other computers log in **once per device** and stay logged in for **90 days** |
| **Always** | This computer needs the password as well |

How to proceed: pick the mode, enter the password **twice** and press **Apply**. It takes effect immediately, and this browser
is signed in at once. There you also find **Sign out all devices** and **Sign out this device**.

What you should know:

- The password is stored only as a **salted hash** (PBKDF2) in `<DataPath>/access.json`. It is not part of the settings
  backup, and **Save** does not change it.
- Wrong entries are throttled per address (5 tries, then 30 seconds, then longer up to 5 minutes at most).
- A small standalone **login page** (`/login`) is shown in the interface language. Everything else, including pages, the live
  connection and the `/api/*` interfaces, needs the login. Only `POST /api/telemetry` keeps its own key (`X-Api-Key`) so Home
  Assistant & co. can keep sending.
- **Forgot the password?** Start WebDesk once with `--reset-password`. That switches the protection off.
  `--reset-password NEWPASSWORD` sets a new password (careful: a password on the command line ends up in your shell
  history). With Docker: `docker exec <container> ./MeshcomWebDesk --reset-password`. Without shell access you start the
  container once with the environment variable `MESHCOM_RESET_ACCESS=1` and remove it again afterwards. A running WebDesk picks
  the change up within a few seconds.
- **Docker:** With `network_mode: host` (as in `docker-compose.image.yml`) a browser on the Docker computer itself counts as
  `localhost`. Other devices need the password.

> ⚠️ Use **HTTPS** where you can (next section). Over plain HTTP the password crosses the network unencrypted. If WebDesk also
> listens on HTTPS, it redirects requests from other devices there. Otherwise the login page warns that the connection is not
> encrypted.

## HTTPS in the home network

HTTPS encrypts the connection and is needed so WebDesk can be **installed as an app** on a phone or tablet. In the home
network you use a **self-signed certificate** issued for your computer's IP address.

**1. Create the certificate** (once):

Windows (PowerShell as administrator, in the application folder):

```powershell
.\scripts\create-lan-cert.ps1                       # detects the IP itself
.\scripts\create-lan-cert.ps1 -LanIp 192.168.1.100  # or give the IP
```

Linux (with `openssl`):

```bash
chmod +x scripts/create-lan-cert.sh
./scripts/create-lan-cert.sh                        # or with IP: ./scripts/create-lan-cert.sh 192.168.1.100
```

The script creates `meshcom-lan.pfx` (for WebDesk) and `meshcom-lan.crt` (for phones and tablets). On Windows it also marks the
certificate as trusted, so the PC shows no warning.

**2. Start WebDesk with HTTPS:** Afterwards WebDesk also listens on port **5163** (HTTP on **5162** stays).

- Started directly: with the launch profile `lan-https` (`dotnet run --launch-profile lan-https`)
- Docker: with the additional file `docker-compose.https.yml`

**3. Trust it on the phone:** Copy `meshcom-lan.crt` to the device and install it as a trusted certificate. Then you open
`https://<IP-of-the-computer>:5163`.

The exact steps, also for Docker, are in the [README](https://github.com/DH1FR/MeshcomWebDesk#readme) in the section
"HTTPS & Zertifikat".

## Installing as an app (PWA)

Over HTTPS WebDesk can be installed like an app: with its own icon, without an address bar and with shortcuts to **Chat** and
**Map**. No store, no download.

- **iPhone / iPad (Safari):** open `https://<IP>:5163`, tap the share icon, **Add to Home Screen**, confirm the name.
- **Android (Chrome):** open `https://<IP>:5163`, menu ⋮ → **Install app** (or follow the banner).
- **Windows / Mac (Chrome or Edge):** click **⊕ Install** in the address bar or menu → **Install MeshCom WebDesk**.

> ⚠️ **Offline does not work:** WebDesk always needs the connection to the server it runs on. Installing the app only changes
> how it looks, not how it works.

## Opening it on the phone: QR code

The **QR** button in the menu bar shows a QR code with WebDesk's address. You scan it with the phone and open the interface.
The dialog points out when **no access protection** is set up.

Continue with [Backup, maintenance and troubleshooting](10-maintenance.md).
