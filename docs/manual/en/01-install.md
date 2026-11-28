# Quick start and installation

From installation to your first received message in four steps:

1. **Install** WebDesk and start it (this chapter)
2. In the interface, enter your **callsign and the node address** ([chapter 2](02-connect-node.md))
3. On the **node**, switch on EXTUDP and point it at the WebDesk computer ([chapter 2](02-connect-node.md))
4. Get to know the interface ([chapter 3](03-interface.md)) and write your first message ([chapter 4](04-writing-messages.md))

## What you need

- A **MeshCom node** with firmware **v4.35 or newer** (EXTUDP must be switched on at the node)
- A computer in the **same network** as the node: Windows, Linux (including Raspberry Pi 4/5) or macOS
- **UDP port 1799** must not be blocked by a firewall

The ready-to-run programs are on [GitHub Releases](https://github.com/DH1FR/MeshcomWebDesk/releases/latest). You do not
have to build anything yourself.

## Windows

### With the installer (recommended)

Download `MeshcomWebDesk-Setup-<version>-win-x64.exe` and run it. The installer brings everything, including the .NET
runtime. During the installation you can choose:

- **Start the Windows service** – WebDesk then runs in the background and starts with Windows
- Create a **desktop shortcut**
- **Open the browser automatically at Windows startup**

Afterwards open WebDesk in the browser at **http://localhost:5162**.

> ⚠️ **SmartScreen warning:** The first time, Windows may show "Windows protected your PC" because the program is not
> code-signed. Click **More info** and then **Run anyway**.

### With the ZIP archive

1. Install the [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Unzip `MeshcomWebDesk-<version>-win-x64.zip` into a folder, e.g. `C:\meshcom`.
3. In `appsettings.json` enter your **node IP** (`DeviceIp`) and your **callsign** (`MyCallsign`).
4. Start `MeshcomWebDesk.exe`. Your default browser opens automatically.

## Linux and Raspberry Pi

### With Docker (recommended)

There are ready-made images for `amd64` and `arm64` (Raspberry Pi 4/5):

```bash
git clone https://github.com/DH1FR/MeshcomWebDesk.git
cd MeshcomWebDesk
docker pull ghcr.io/dh1fr/meshcomwebdesk:latest
cp deploy/appsettings.linux.json appsettings.json
nano appsettings.json          # enter DeviceIp and MyCallsign
docker compose up -d
```

The interface is at **http://\<Linux-IP\>:5162**. The container starts by itself after a reboot
(`restart: unless-stopped`).

> ℹ️ The container runs with `network_mode: host` so it receives the UDP packets from the node.
> Settings you save in the interface end up in the `./data` folder and survive updates.

### Without Docker (systemd)

```bash
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0
tar -xzf MeshcomWebDesk-<version>-linux-x64.tar.gz
cd MeshcomWebDesk
nano appsettings.json          # MyCallsign, DeviceIp
sudo bash install.sh           # sets up the service, starts at boot
```

On a Raspberry Pi use `linux-arm64` instead of `linux-x64`.

Useful commands afterwards:

```bash
journalctl -u meshcom-webdesk -f     # live log
systemctl status meshcom-webdesk     # status
systemctl restart meshcom-webdesk    # restart after changes
```

## macOS

Install the .NET 10 runtime, unpack the matching archive (`osx-arm64` for Apple Silicon, `osx-x64` for Intel), enter
`MyCallsign` and `DeviceIp` in `appsettings.json` and start `MeshcomWebDesk`. The interface is at
**http://localhost:5162**.

## Reaching it from other devices

By default WebDesk listens on port **5162**. From a phone or tablet in the same network you reach it at
`http://<IP-of-the-computer>:5162`. In WebDesk the **QR** button in the menu shows a QR code that you scan with your
phone.

> 🔒 WebDesk is open to everybody who knows the address. If other people in the network could read or send, set a
> password under **Settings → System → Access protection**.

## Updating

- **Windows, Linux, macOS without Docker:** On the page **About → Update / backups** (`/update`) WebDesk updates itself
  and creates a backup first.
- **Docker:** The image is replaced, not the files. Either with one click on the update page (with
  `docker-compose.image.yml` and Watchtower) or with one command, e.g. `sudo ./scripts/update-docker.sh --image`. All
  ways are described in the [Docker update guide](https://github.com/DH1FR/MeshcomWebDesk/blob/master/docs/docker-update-en.md).
  Your data in `./data`, `./logs` and `./keys` is kept.

Continue with [Connecting the node](02-connect-node.md).
