#!/usr/bin/env bash
# start-https.sh
# Startet MeshCom WebDesk mit HTTP + HTTPS.
# Prueft zuerst ob das Zertifikat vorhanden ist.
#
# Verwendung:
#   chmod +x scripts/start-https.sh
#   ./scripts/start-https.sh               # start / recreate
#   ./scripts/start-https.sh --build       # rebuild image

CERT_FILE="certs/meshcom-lan.pfx"

# ── Zertifikat pruefen ────────────────────────────────────────────────────────
if [ ! -f "$CERT_FILE" ]; then
    echo ""
    echo "FEHLER: Zertifikat nicht gefunden: $CERT_FILE"
    echo ""
    echo "Bitte zuerst erzeugen:"
    echo "  ./scripts/create-lan-cert.sh"
    echo ""
    exit 1
fi

echo "Zertifikat gefunden: $CERT_FILE"
echo "Starte MeshCom WebDesk mit HTTP + HTTPS..."
echo ""

exec docker compose \
    -f docker-compose.yml \
    -f docker-compose.https.yml \
    up -d "$@"
