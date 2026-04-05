#!/bin/bash
# MeshCom WebDesk – Linux install script
# Usage: sudo bash install.sh

set -e

INSTALL_DIR=/opt/meshcom
SERVICE_NAME=meshcom-webdesk
LOG_DIR=/var/log/meshcom
USER=meshcom

echo "=== MeshCom WebDesk Installer ==="

# Create user
if ! id "$USER" &>/dev/null; then
    useradd -r -s /bin/false $USER
    echo "[+] User '$USER' created"
fi

# Create directories
mkdir -p $INSTALL_DIR $LOG_DIR
chown $USER:$USER $INSTALL_DIR $LOG_DIR

# Copy files
cp -r ./* $INSTALL_DIR/

# Use platform config if available (source clone), otherwise use bundled appsettings.json (binary release)
if [ -f "appsettings.linux.json" ]; then
    cp appsettings.linux.json $INSTALL_DIR/appsettings.json
    echo "[+] Config: appsettings.linux.json"
else
    echo "[+] Config: appsettings.json (bitte MyCallsign und DeviceIp prüfen!)"
fi

chmod +x $INSTALL_DIR/MeshcomWebDesk
chown -R $USER:$USER $INSTALL_DIR

# Install systemd service
cp meshcom-webdesk.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable $SERVICE_NAME
systemctl start $SERVICE_NAME

echo ""
echo "=== Installation complete ==="
echo "Service status: $(systemctl is-active $SERVICE_NAME)"
echo "Web interface:  http://$(hostname -I | awk '{print $1}'):5162"
echo "Logs:           journalctl -u $SERVICE_NAME -f"
echo "Log files:      $LOG_DIR"
