#!/usr/bin/env bash
# update-docker.sh
# Brings a Docker installation of MeshCom WebDesk up to date in one command.
#
#   ./scripts/update-docker.sh            # built from source: git pull + rebuild + restart
#   ./scripts/update-docker.sh --image    # published image (docker-compose.image.yml): pull + restart
#
# HTTPS (certs/meshcom-lan.pfx) is used automatically when the certificate exists; force it off with --http.
# Run it with sudo if your user may not use docker. Your data in ./data, ./logs and ./keys is kept.

set -euo pipefail
cd "$(dirname "$0")/.."

MODE=source
HTTPS=auto
for arg in "$@"; do
    case "$arg" in
        --image) MODE=image ;;
        --https) HTTPS=yes ;;
        --http)  HTTPS=no ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done

if [ "$MODE" = image ]; then
    FILES=(-f docker-compose.image.yml)
else
    FILES=(-f docker-compose.yml)
fi

if [ "$HTTPS" = auto ] && [ -f certs/meshcom-lan.pfx ]; then HTTPS=yes; fi
if [ "$HTTPS" = yes ]; then
    if [ ! -f certs/meshcom-lan.pfx ]; then
        echo "ERROR: certs/meshcom-lan.pfx not found - create it with ./scripts/create-lan-cert.sh"
        exit 1
    fi
    FILES+=(-f docker-compose.https.yml)
fi

if [ "$MODE" = image ]; then
    echo "Pulling the newest image ..."
    docker compose "${FILES[@]}" pull
    echo "Restarting ..."
    docker compose "${FILES[@]}" up -d
else
    BRANCH="$(git rev-parse --abbrev-ref HEAD)"
    echo "Updating the source (branch $BRANCH) ..."
    if ! git diff --quiet || ! git diff --cached --quiet; then
        echo "ERROR: you have local changes - git pull would fail. Keep your settings in a docker-compose.override.yml"
        echo "       or run 'git stash' first, then this script, then 'git stash pop'."
        git status --short
        exit 1
    fi
    git pull --ff-only origin "$BRANCH"
    echo "Building and restarting ..."
    docker compose "${FILES[@]}" up -d --build
fi

echo
echo "Done. Running version:"
docker logs --tail 40 meshcom-webdesk 2>&1 | grep -m1 -E "v[0-9]+\.[0-9]+" || echo "(see: docker logs meshcom-webdesk)"
docker image prune -f >/dev/null 2>&1 || true
